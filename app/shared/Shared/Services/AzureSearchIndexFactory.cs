// Copyright (c) Microsoft. All rights reserved.

using Azure.Search.Documents.Indexes.Models;

namespace Shared.Services;
internal class AzureSearchIndexFactory
{
    private static (string ConfigName, string Profile, int DocsDimensions, int? ImageDimensions)
        s_vectorSearch = ("my-vector-config", "my-vector-profile", 1536, null);
    public AzureSearchIndexFactory(IComputerVisionService? computerVisionService = null, bool includeImageEmbeddingsField = false)
    {
        if (includeImageEmbeddingsField && computerVisionService is null)
        {
            throw new InvalidOperationException("Computer Vision service is required to include image embeddings field");
        }
        else
        {
            s_vectorSearch.ImageDimensions = computerVisionService!.Dimension;
        }
    }
    public SearchIndex CreateIndex(string name)
    {
        var index = new SearchIndex(name)
        {
            VectorSearch = CreateVector(),
            Fields = CreateFields(),
            SemanticSearch = CreateSemantic()
        };
        //
        return index;
    }

    private VectorSearch CreateVector() => new()
    {
        Algorithms = { new HnswAlgorithmConfiguration(s_vectorSearch.ConfigName) },
        Profiles = { new VectorSearchProfile(s_vectorSearch.Profile, s_vectorSearch.ConfigName) }
    };

    private SemanticSearch CreateSemantic() => new()
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
    };

    private IList<SearchField> CreateFields()
    {
        IList<SearchField> fields = [
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
            new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
            new SimpleField("category", SearchFieldDataType.String) { IsFacetable = true },
            new SimpleField("sourcepage", SearchFieldDataType.String) { IsFacetable = true },
            new SimpleField("sourcefile", SearchFieldDataType.String) { IsFacetable = true },
            new SearchField("embedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    VectorSearchDimensions = s_vectorSearch.DocsDimensions,
                    IsSearchable = true,
                    VectorSearchProfileName = s_vectorSearch.Profile,
                }
            ];

        if (s_vectorSearch.ImageDimensions.HasValue)
        {
            fields.Add(new SearchField("imageEmbedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                VectorSearchDimensions = s_vectorSearch.ImageDimensions.Value,
                IsSearchable = true,
                VectorSearchProfileName = s_vectorSearch.Profile,
            });
        }

        return fields;
    }
}
