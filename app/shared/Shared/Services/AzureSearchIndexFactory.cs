// Copyright (c) Microsoft. All rights reserved.

using Azure.Search.Documents.Indexes.Models;

namespace Shared.Services;
internal class AzureSearchIndexFactory
{
    public AzureSearchIndexFactory(IComputerVisionService? computerVisionService = null, bool includeImageEmbeddingsField = false)
    {
        if (includeImageEmbeddingsField && computerVisionService is null)
        {
            throw new InvalidOperationException("Computer Vision service is required to include image embeddings field");
        }
        else
        {
            DemoConstants.VectorSearch.ImageDimensions = computerVisionService!.Dimension;
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
        Algorithms = { new HnswAlgorithmConfiguration(DemoConstants.VectorSearch.ConfigName) },
        Profiles = { new VectorSearchProfile(DemoConstants.VectorSearch.Profile, DemoConstants.VectorSearch.ConfigName) }
    };

    private SemanticSearch CreateSemantic() => new()
    {
        Configurations =
        {
            new SemanticConfiguration(DemoConstants.Semantic.ConfigName, new()
            {
                ContentFields =
                {
                    new SemanticField(DemoConstants.Semantic.SearchableField)
                }
            })
        }
    };

    private IList<SearchField> CreateFields()
    {
        IList<SearchField> fields = [
            new SimpleField(DemoConstants.SimpleFields.Id, SearchFieldDataType.String) { IsKey = true },
            new SearchableField(DemoConstants.Semantic.SearchableField) { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
            new SimpleField(DemoConstants.SimpleFields.Category, SearchFieldDataType.String) { IsFacetable = true },
            new SimpleField(DemoConstants.SimpleFields.SourcePage, SearchFieldDataType.String) { IsFacetable = true },
            new SimpleField(DemoConstants.SimpleFields.SourceFile, SearchFieldDataType.String) { IsFacetable = true },
            new SearchField(DemoConstants.EmbeddingFields.Docs, SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    VectorSearchDimensions =  DemoConstants.VectorSearch.DocsDimensions,
                    IsSearchable = true,
                    VectorSearchProfileName =  DemoConstants.VectorSearch.Profile,
                }
            ];

        if (DemoConstants.VectorSearch.ImageDimensions.HasValue)
        {
            fields.Add(new SearchField(DemoConstants.EmbeddingFields.Images, SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                VectorSearchDimensions = DemoConstants.VectorSearch.ImageDimensions.Value,
                IsSearchable = true,
                VectorSearchProfileName = DemoConstants.VectorSearch.Profile,
            });
        }

        return fields;
    }
}
