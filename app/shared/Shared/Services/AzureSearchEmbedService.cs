// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using System.Text.RegularExpressions;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
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
    private static (string ConfigName, string Profile, int DocsDimensions) s_vectorSearch = ("my-vector-config", "my-vector-profile", 1536);
    private readonly AzureSearchIndexFactory _indexFactory = new(computerVisionService, includeImageEmbeddingsField);

    public async Task<bool> EmbedPdfBlobAsync(Stream pdfBlobStream, string blobName)
    {
        try
        {
            await EnsureSearchIndexAsync(searchIndexName);
            Console.WriteLine($"Embedding blob '{blobName}'");
            var pageDetails = await GetDocumentTextAsync(pdfBlobStream, blobName);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(blobName);

            // Create corpus from page map and upload to blob
            // Corpus name format: fileName-{page}.txt
            foreach (var page in pageDetails)
            {
                var corpusName = $"{fileNameWithoutExtension}-{page.Index}.txt";
                await UploadCorpusAsync(corpusName, page.Text);
            }

            var docsSections = pageDetails.CreateDocsSections(blobName, MatchInSetRegex(), logger);

            var infoLoggingEnabled = logger?.IsEnabled(LogLevel.Information);
            if (infoLoggingEnabled is true)
            {
                logger?.LogInformation("""
                Indexing sections from '{BlobName}' into search index '{SearchIndexName}'
                """,
                    blobName,
                    searchIndexName);
            }

            await IndexSectionsAsync(docsSections);

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
        var imgSection = AzureDataAnalysisExtension.CreateImageSection(imageUrl, imageName, MatchInSetRegex());
        // step 3
        // index image embeddings
        var indexAction = imgSection.CreateImageIndexAction(embeddings.vector);

        var batch = new IndexDocumentsBatch<SearchDocument>();
        batch.Actions.Add(indexAction);
        await searchClient.IndexDocumentsAsync(batch, cancellationToken: ct);

        return true;
    }

    public async Task CreateSearchIndexAsync(string searchIndexName, CancellationToken ct = default)
    {
        var index = _indexFactory.CreateIndex(searchIndexName);

        logger?.LogInformation(
            "Creating '{searchIndexName}' search index", searchIndexName);
        //
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
        return documentAnalysisClient.AnalyzeDocumentPagesAsync(blobStream, blobName, logger);
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
            batch.Actions.Add(section.CreateDocsIndexAction(embedding));

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
