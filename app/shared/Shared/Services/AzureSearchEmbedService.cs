// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using System.Text.RegularExpressions;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Shared.Extensions;
using Shared.Models;

namespace Shared.Services;
public sealed partial class AzureSearchEmbedService(
    OpenAIClient openAIClient,
    string embeddingModelName,
    SearchClient searchClient,
    string searchIndexName,
    SearchIndexClient searchIndexClient,
    DocumentAnalysisClient documentAnalysisClient,
    BlobContainerClient corpusContainerClient,
    IComputerVisionService? computerVisionService = null,
    bool includeImageEmbeddingsField = false,
    ILogger<AzureSearchEmbedService>? logger = null) : IEmbedService
{
    [GeneratedRegex("[^0-9a-zA-Z_-]")]
    private static partial Regex MatchInSetRegex();

    public async Task<bool> EmbedPdfBlobAsync(Stream pdfBlobStream, string blobName)
    {
        try
        {
            await EnsureSearchIndexAsync(searchIndexName);
            Console.WriteLine($"Embedding blob '{blobName}'");
            var pageMap = await GetDocumentTextAsync(pdfBlobStream, blobName);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(blobName);

            // Create corpus from page map and upload to blob
            // Corpus name format: fileName-{page}.txt
            foreach (var page in pageMap)
            {
                var corpusName = $"{fileNameWithoutExtension}-{page.Index}.txt";
                await UploadCorpusAsync(corpusName, page.Text);
            }

            var sections = pageMap.CreateSections(blobName, MatchInSetRegex(), logger);

            var infoLoggingEnabled = logger?.IsEnabled(LogLevel.Information);
            if (infoLoggingEnabled is true)
            {
                logger?.LogInformation("""
                Indexing sections from '{BlobName}' into search index '{SearchIndexName}'
                """,
                    blobName,
                    searchIndexName);
            }

            await IndexSectionsAsync(sections);

            return true;
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception, "Failed to embed blob '{BlobName}'", blobName);

            throw;
        }
    }

    public async Task<bool> EmbedImageBlobAsync(
        Stream imageStream,
        string imageUrl,
        string imageName,
        CancellationToken ct = default)
    {
        if (includeImageEmbeddingsField == false || computerVisionService is null)
        {
            throw new InvalidOperationException(
                "Computer Vision service is required to include image embeddings field, please enable GPT_4V support");
        }

        var embeddings = await computerVisionService.VectorizeImageAsync(imageUrl, ct);

        // id can only contain letters, digits, underscore (_), dash (-), or equal sign (=).
        var imageId = MatchInSetRegex().Replace(imageUrl, "_").TrimStart('_');
        // step 3
        // index image embeddings
        var indexAction = new IndexDocumentsAction<SearchDocument>(
            IndexActionType.MergeOrUpload,
            new SearchDocument
            {
                ["id"] = imageId,
                ["content"] = imageName,
                ["category"] = "image",
                ["imageEmbedding"] = embeddings.vector,
                ["sourcefile"] = imageUrl,
            });

        var batch = new IndexDocumentsBatch<SearchDocument>();
        batch.Actions.Add(indexAction);
        await searchClient.IndexDocumentsAsync(batch, cancellationToken: ct);

        return true;
    }

    public async Task CreateSearchIndexAsync(string searchIndexName, CancellationToken ct = default)
    {
        string vectorSearchConfigName = "my-vector-config";
        string vectorSearchProfile = "my-vector-profile";
        var index = new SearchIndex(searchIndexName)
        {
            VectorSearch = new()
            {
                Algorithms =
                {
                    new HnswAlgorithmConfiguration(vectorSearchConfigName)
                },
                Profiles =
                {
                    new VectorSearchProfile(vectorSearchProfile, vectorSearchConfigName)
                }
            },
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SimpleField("category", SearchFieldDataType.String) { IsFacetable = true },
                new SimpleField("sourcepage", SearchFieldDataType.String) { IsFacetable = true },
                new SimpleField("sourcefile", SearchFieldDataType.String) { IsFacetable = true },
                new SearchField("embedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    VectorSearchDimensions = 1536,
                    IsSearchable = true,
                    VectorSearchProfileName = vectorSearchProfile,
                }
            },
            SemanticSearch = new()
            {
                Configurations =
                {
                    new SemanticConfiguration("default", new()
                    {
                        ContentFields =
                        {
                            new SemanticField("content")
                        }
                    })
                }
            }
        };

        logger?.LogInformation(
            "Creating '{searchIndexName}' search index", searchIndexName);

        if (includeImageEmbeddingsField)
        {
            if (computerVisionService is null)
            {
                throw new InvalidOperationException("Computer Vision service is required to include image embeddings field");
            }

            index.Fields.Add(new SearchField("imageEmbedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                VectorSearchDimensions = computerVisionService.Dimension,
                IsSearchable = true,
                VectorSearchProfileName = vectorSearchProfile,
            });
        }
        await searchIndexClient.CreateIndexAsync(index, ct);
    }

    public async Task EnsureSearchIndexAsync(string searchIndexName, CancellationToken ct = default)
    {
        var indexNames = searchIndexClient.GetIndexNamesAsync(ct);
        await foreach (var page in indexNames.AsPages())
        {
            if (page.Values.Any(indexName => indexName == searchIndexName))
            {
                logger?.LogWarning(
                    "Search index '{SearchIndexName}' already exists", searchIndexName);
                return;
            }
        }

        await CreateSearchIndexAsync(searchIndexName, ct);
    }

    public Task<IReadOnlyList<PageDetail>> GetDocumentTextAsync(Stream blobStream, string blobName)
    {
        return documentAnalysisClient.GetDocumentTextAsync(blobStream, blobName, logger);
    }

    private async Task UploadCorpusAsync(string corpusBlobName, string text)
    {
        var blobClient = corpusContainerClient.GetBlobClient(corpusBlobName);
        if (await blobClient.ExistsAsync())
        {
            return;
        }

        logger?.LogInformation("Uploading corpus '{CorpusBlobName}'", corpusBlobName);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await blobClient.UploadAsync(stream, new BlobHttpHeaders
        {
            ContentType = "text/plain"
        });
    }

    private async Task IndexSectionsAsync(IEnumerable<Section> sections)
    {
        var iteration = 0;
        var batch = new IndexDocumentsBatch<SearchDocument>();
        foreach (var section in sections)
        {
            var embeddings = await openAIClient.GetEmbeddingsAsync(new Azure.AI.OpenAI.EmbeddingsOptions(embeddingModelName, [section.Content.Replace('\r', ' ')]));
            var embedding = embeddings.Value.Data.FirstOrDefault()?.Embedding.ToArray() ?? [];
            batch.Actions.Add(section.CreateIndex(embedding));

            iteration++;
            if (iteration % 1_000 is 0)
            {
                // Every one thousand documents, batch create.
                await BatchIndexDocumentsAsync(batch);
                batch = new();
            }
        }

        if (batch is { Actions.Count: > 0 })
        {
            await BatchIndexDocumentsAsync(batch);
        }
    }


    private async Task BatchIndexDocumentsAsync(IndexDocumentsBatch<SearchDocument> batch)
    {
        IndexDocumentsResult result = await searchClient.IndexDocumentsAsync(batch);
        int succeeded = result.Results.Count(r => r.Succeeded);
        if (logger?.IsEnabled(LogLevel.Information) is true)
        {
            var message = $"Indexed {batch.Actions.Count} sections, {succeeded} succeeded";
            logger?.LogInformation(message);
        }
    }
}
