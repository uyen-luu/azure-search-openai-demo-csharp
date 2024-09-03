// Copyright (c) Microsoft. All rights reserved.

using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Shared.Models;

namespace Shared.Extensions;

internal class DocumentQueryBuilder
{
    public static SearchOptions CreateSearchDocOptions(RequestOverrides? overrides, float[]? embedding)
    {
        var top = overrides?.Top ?? 3;
        var exclude_category = overrides?.ExcludeCategory;
        var filter = exclude_category == null ? string.Empty : $"category ne '{exclude_category}'";
        var useSemanticRanker = overrides?.SemanticRanker ?? false;
        var useSemanticCaptions = overrides?.SemanticCaptions ?? false;

        SearchOptions searchOptions = useSemanticRanker
            ? new SearchOptions
            {
                Filter = filter,
                QueryType = SearchQueryType.Semantic,
                SemanticSearch = new()
                {
                    SemanticConfigurationName = "default",
                    QueryCaption = new(useSemanticCaptions
                        ? QueryCaptionType.Extractive
                        : QueryCaptionType.None),
                },
                // TODO: Find if these options are assignable
                //QueryLanguage = "en-us",
                //QuerySpeller = "lexicon",
                Size = top,
            }
            : new SearchOptions
            {
                Filter = filter,
                Size = top,
            };

        if (embedding != null && overrides?.RetrievalMode != RetrievalMode.Text)
        {
            var k = useSemanticRanker ? 50 : top;
            var vectorQuery = new VectorizedQuery(embedding)
            {
                // if semantic ranker is enabled, we need to set the rank to a large number to get more
                // candidates for semantic reranking
                KNearestNeighborsCount = useSemanticRanker ? 50 : top,
            };
            vectorQuery.Fields.Add("embedding");
            searchOptions.VectorSearch = new();
            searchOptions.VectorSearch.Queries.Add(vectorQuery);
        }
        return searchOptions;
    }

    public static SearchOptions CreateSearchImageOptions(RequestOverrides? overrides, float[]? embedding)
    {
        var top = overrides?.Top ?? 3;
        var exclude_category = overrides?.ExcludeCategory;
        var filter = exclude_category == null ? string.Empty : $"category ne '{exclude_category}'";

        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Size = top,
        };

        if (embedding != null)
        {
            var vectorQuery = new VectorizedQuery(embedding)
            {
                KNearestNeighborsCount = top,
            };
            vectorQuery.Fields.Add("imageEmbedding");
            searchOptions.VectorSearch = new();
            searchOptions.VectorSearch.Queries.Add(vectorQuery);
        }
        return searchOptions;
    }
}
