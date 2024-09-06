// Copyright (c) Microsoft. All rights reserved.

using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Shared.Extensions;
using Shared.Models;

namespace Shared.Services;

public class AzureSearchService(SearchClient searchClient) : ISearchService
{
    public async Task<SupportingContentRecord[]> QueryDocumentsAsync(string? query = null,
                                                                    float[]? embedding = null,
                                                                    RequestOverrides? overrides = null,
                                                                    CancellationToken ct = default)
    {
        if (query is null && embedding is null)
        {
            throw new ArgumentException("Either query or embedding must be provided");
        }
        //
        var options = DocumentQueryBuilder.CreateSearchDocOptions(overrides, embedding);
        var result = await SearchAsync(query, options, ct);
        //
        var useSemanticCaptions = overrides?.SemanticCaptions ?? false;
        var sb = new List<SupportingContentRecord>();
        foreach (var doc in result.GetResults())
        {
            TryProcessDoc(ref sb, doc, useSemanticCaptions);
        }

        return [.. sb];
    }

    public async Task<SupportingImageRecord[]> QueryImagesAsync(string? query = null,
                                                                float[]? embedding = null,
                                                                RequestOverrides? overrides = null,
                                                                CancellationToken ct = default)
    {
        var options = DocumentQueryBuilder.CreateSearchImageOptions(overrides, embedding);
        var result = await SearchAsync(query, options, ct);
        var sb = new List<SupportingImageRecord>();
        foreach (var doc in result.GetResults())
        {
            TryProcessImage(ref sb, doc);
        }

        return [.. sb];
    }

    #region Privates
    private static void TryProcessDoc(ref List<SupportingContentRecord> sb, SearchResult<SearchDocument> doc, bool useSemanticCaptions)
    {
        doc.Document.TryGetValue(DemoConstants.SimpleFields.SourcePage, out var sourcePageValue);
        string? contentValue;
        try
        {
            if (useSemanticCaptions)
            {
                var docs = doc.SemanticSearch.Captions.Select(c => c.Text);
                contentValue = string.Join(" . ", docs);
            }
            else
            {
                doc.Document.TryGetValue(DemoConstants.Semantic.SearchableField, out var value);
                contentValue = (string)value;
            }
        }
        catch (ArgumentNullException)
        {
            contentValue = null;
        }

        if (sourcePageValue is string sourcePage && contentValue is string content)
        {
            content = content.Replace('\r', ' ').Replace('\n', ' ');
            sb.Add(new SupportingContentRecord(sourcePage, content));
        }
    }

    private static void TryProcessImage(ref List<SupportingImageRecord> sb, SearchResult<SearchDocument> doc)
    {
        doc.Document.TryGetValue(DemoConstants.SimpleFields.SourceFile, out var sourceFileValue);
        doc.Document.TryGetValue(DemoConstants.EmbeddingFields.Images, out var imageEmbeddingValue);
        doc.Document.TryGetValue(DemoConstants.SimpleFields.Category, out var categoryValue);
        doc.Document.TryGetValue(DemoConstants.Semantic.SearchableField, out var imageName);
        if (sourceFileValue is string url &&
            imageName is string name &&
            categoryValue is string category &&
            category == DemoConstants.Category.Images)
        {
            sb.Add(new SupportingImageRecord(name, url));
        }
    }


    // Assemble sources here.
    // Example output for each SearchDocument:
    // {
    //   "@search.score": 11.65396,
    //   "id": "Northwind_Standard_Benefits_Details_pdf-60",
    //   "content": "x-ray, lab, or imaging service, you will likely be responsible for paying a copayment or coinsurance. The exact amount you will be required to pay will depend on the type of service you receive. You can use the Northwind app or website to look up the cost of a particular service before you receive it.\nIn some cases, the Northwind Standard plan may exclude certain diagnostic x-ray, lab, and imaging services. For example, the plan does not cover any services related to cosmetic treatments or procedures. Additionally, the plan does not cover any services for which no diagnosis is provided.\nIt’s important to note that the Northwind Standard plan does not cover any services related to emergency care. This includes diagnostic x-ray, lab, and imaging services that are needed to diagnose an emergency condition. If you have an emergency condition, you will need to seek care at an emergency room or urgent care facility.\nFinally, if you receive diagnostic x-ray, lab, or imaging services from an out-of-network provider, you may be required to pay the full cost of the service. To ensure that you are receiving services from an in-network provider, you can use the Northwind provider search ",
    //   "category": null,
    //   "sourcepage": "Northwind_Standard_Benefits_Details-24.pdf",
    //   "sourcefile": "Northwind_Standard_Benefits_Details.pdf"
    // }
    private async Task<SearchResults<SearchDocument>> SearchAsync(string? query, SearchOptions options, CancellationToken ct = default)
    {
        var result = await searchClient.SearchAsync<SearchDocument>(query, options, ct);

        if (result.Value is null)
        {
            throw new InvalidOperationException("fail to get search result");
        }
        return result.Value;
    }
    #endregion
}
