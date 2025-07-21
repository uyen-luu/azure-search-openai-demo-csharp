// Copyright (c) Microsoft. All rights reserved.

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Shared.Extensions;
internal static class AzureDataAnalysisExtension
{
    private const int MaxSectionLength = 1_000;
    private const int SentenceSearchLimit = 100;
    private const int SectionOverlap = 100;
    private readonly static char[] s_sentenceEndings = ['.', '!', '?'];
    private readonly static char[] s_wordBreaks = [',', ';', ':', ' ', '(', ')', '[', ']', '{', '}', '\t', '\n'];
    public static async Task<IReadOnlyList<PageDetail>> AnalyzeDocumentPagesAsync(this DocumentAnalysisClient client,
                                                                                  Stream blobStream,
                                                                                  string blobName,
                                                                                  ILogger? logger = null,
                                                                                  CancellationToken ct = default)
    {
        logger?.LogInformation(
            "Extracting text from '{Blob}' using Azure Form Recognizer", blobName);
        //
        using var ms = new MemoryStream();
        blobStream.CopyTo(ms);
        ms.Position = 0;
        AnalyzeDocumentOperation operation = client.AnalyzeDocument(WaitUntil.Started, "prebuilt-layout", ms, cancellationToken: ct);
        var offset = 0;
        List<PageDetail> pageMap = [];

        var analyzeResult = await operation.WaitForCompletionAsync(ct);
        var pages = analyzeResult.Value.Pages;
        for (var i = 0; i < pages.Count; i++)
        {
            var pageDetail = analyzeResult.GetPageDetail(pages, i, offset);

            pageMap.Add(pageDetail);
            offset += pageDetail.Text.Length;
        }
        return pageMap.AsReadOnly();
    }

    public static IEnumerable<Section> CreateDocsSections(this IReadOnlyList<PageDetail> pageMap,
                                                          string blobName,
                                                          Regex matchInSetRegex,
                                                          ILogger? logger = null)
    {
        var allText = string.Concat(pageMap.Select(p => p.Text))!;
        var length = allText.Length;
        var start = 0;
        var end = length;

        logger?.LogInformation("Splitting '{BlobName}' into sections", blobName);

        while (start + SectionOverlap < length)
        {
            end = allText.TryFindEnd(start, ref end, length);
            start = allText.TryFindStart(ref start, end);
            //
            var sectionText = allText[start..end];

            yield return new Section(
                Id: matchInSetRegex.Replace($"{blobName}-{start}", "_").TrimStart('_'),
                Content: sectionText,
                SourcePage: BlobNameFromFilePage(blobName, pageMap.FindPage(start)),
                SourceFile: blobName);

            var lastTableStart = sectionText.LastIndexOf("<table", StringComparison.Ordinal);
            if (lastTableStart > 2 * SentenceSearchLimit && lastTableStart > sectionText.LastIndexOf("</table", StringComparison.Ordinal))
            {
                // If the section ends with an unclosed table, we need to start the next section with the table.
                // If table starts inside SentenceSearchLimit, we ignore it, as that will cause an infinite loop for tables longer than MaxSectionLength
                // If last table starts inside SectionOverlap, keep overlapping
                if (logger?.IsEnabled(LogLevel.Warning) is true)
                {
                    logger?.LogWarning("""
                        Section ends with unclosed table, starting next section with the
                        table at page {Offset} offset {Start} table start {LastTableStart}
                        """,
                        FindPage(pageMap, start),
                        start,
                        lastTableStart);
                }

                start = Math.Min(end - SectionOverlap, start + lastTableStart);
            }
            else
            {
                start = end - SectionOverlap;
            }
        }

        if (start + SectionOverlap < end)
        {
            yield return new Section(
                Id: matchInSetRegex.Replace($"{blobName}-{start}", "_").TrimStart('_'),
                Content: allText[start..end],
                SourcePage: BlobNameFromFilePage(blobName, pageMap.FindPage(start)),
                SourceFile: blobName);
        }
    }


    public static Section CreateImageSection(string imageUrl,
                                             string imageName,
                                             Regex matchInSetRegex)
    {
        return new Section(
            Id: matchInSetRegex.Replace(imageUrl, "_").TrimStart('_'),   // id can only contain letters, digits, underscore (_), dash (-), or equal sign (=).
            Content: imageName,
            Category: DemoConstants.Category.Images,
            SourcePage: imageUrl,
            SourceFile: imageUrl);
    }


    public static IndexDocumentsAction<SearchDocument> CreateDocsIndexAction(this Section section, float[]? embedding)
    {
        return new IndexDocumentsAction<SearchDocument>(
                IndexActionType.MergeOrUpload,
                new SearchDocument
                {
                    [DemoConstants.SimpleFields.Id] = section.Id,
                    [DemoConstants.Semantic.SearchableField] = section.Content,
                    [DemoConstants.SimpleFields.Category] = section.Category,
                    [DemoConstants.SimpleFields.SourcePage] = section.SourcePage,
                    [DemoConstants.SimpleFields.SourceFile] = section.SourceFile,
                    [DemoConstants.EmbeddingFields.Docs] = embedding,
                });
    }

    public static IndexDocumentsAction<SearchDocument> CreateImageIndexAction(this Section section, float[]? embedding)
    {
        return new IndexDocumentsAction<SearchDocument>(
                IndexActionType.MergeOrUpload,
                new SearchDocument
                {
                    [DemoConstants.SimpleFields.Id] = section.Id,
                    [DemoConstants.Semantic.SearchableField] = section.Content,
                    [DemoConstants.SimpleFields.Category] = section.Category,
                    [DemoConstants.SimpleFields.SourceFile] = section.SourceFile,
                    [DemoConstants.EmbeddingFields.Images] = embedding,
                });
    }

    #region Privates
    private static PageDetail GetPageDetail(this Response<AnalyzeResult> results, IReadOnlyList<DocumentPage> pages, int i, int offset)
    {
        IReadOnlyList<DocumentTable> tablesOnPage =
                results.Value.Tables.Where(t => t.BoundingRegions[0].PageNumber == i + 1).ToList();

        // Mark all positions of the table spans in the page
        var pageSpan = pages[i].Spans[0];
        int[] tableChars = tablesOnPage.CalculateChars(pageSpan);

        // Build page text by replacing characters in table spans with table HTML
        var pageText = results.CalculateText(tablesOnPage, pageSpan, tableChars);

        return new PageDetail(i, offset, pageText);
    }

    private static int[] CalculateChars(this IReadOnlyList<DocumentTable> tablesOnPage, DocumentSpan pageSpan)
    {
        int[] tableChars = Enumerable.Repeat(-1, pageSpan.Length).ToArray();
        for (var tIndex = 0; tIndex < tablesOnPage.Count; tIndex++)
        {
            foreach (DocumentSpan tableSpan in tablesOnPage[tIndex].Spans)
            {
                // Replace all table spans with "tableId" in tableChars array
                for (var j = 0; j < tableSpan.Length; j++)
                {
                    int index = tableSpan.Index - pageSpan.Index + j;
                    if (index >= 0 && index < pageSpan.Length)
                    {
                        tableChars[index] = tIndex;
                    }
                }
            }
        }

        return tableChars;
    }

    private static string CalculateText(this Response<AnalyzeResult> results,
                                        IReadOnlyList<DocumentTable> tablesOnPage,
                                        DocumentSpan pageSpan,
                                        int[] tableChars)
    {
        // Build page text by replacing characters in table spans with table HTML
        StringBuilder pageText = new();
        HashSet<int> addedTables = [];
        for (int j = 0; j < tableChars.Length; j++)
        {
            if (tableChars[j] == -1)
            {
                pageText.Append(results.Value.Content[pageSpan.Index + j]);
            }
            else if (!addedTables.Contains(tableChars[j]))
            {
                pageText.Append(tablesOnPage[tableChars[j]].ToHtml());
                addedTables.Add(tableChars[j]);
            }
        }
        pageText.Append(' ');
        return pageText.ToString();
    }

    private static string ToHtml(this DocumentTable table)
    {
        var tableHtml = new StringBuilder("<table>");
        var rows = new List<DocumentTableCell>[table.RowCount];
        for (int i = 0; i < table.RowCount; i++)
        {
            rows[i] =
            [
                .. table.Cells.Where(c => c.RowIndex == i)
                                .OrderBy(c => c.ColumnIndex)
,
            ];
        }

        foreach (var rowCells in rows)
        {
            rowCells.AppendRow(ref tableHtml);
        }

        tableHtml.Append("</table>");

        return tableHtml.ToString();
    }

    private static void AppendRow(this List<DocumentTableCell> rowCells, ref StringBuilder tableHtml)
    {
        tableHtml.Append("<tr>");
        foreach (DocumentTableCell cell in rowCells)
        {
            var tag = cell.Kind == "columnHeader" || cell.Kind == "rowHeader" ? "th" : "td";
            var cellSpans = string.Empty;
            if (cell.ColumnSpan > 1)
            {
                cellSpans += $" colSpan='{cell.ColumnSpan}'";
            }

            if (cell.RowSpan > 1)
            {
                cellSpans += $" rowSpan='{cell.RowSpan}'";
            }

            tableHtml.AppendFormat(
                "<{0}{1}>{2}</{0}>", tag, cellSpans, WebUtility.HtmlEncode(cell.Content));
        }

        tableHtml.Append("</tr>");
    }

    private static int FindPage(this IReadOnlyList<PageDetail> pageMap, int offset)
    {
        var length = pageMap.Count;
        for (var i = 0; i < length - 1; i++)
        {
            if (offset >= pageMap[i].Offset && offset < pageMap[i + 1].Offset)
            {
                return i;
            }
        }

        return length - 1;
    }

    private static string BlobNameFromFilePage(string blobName, int page = 0) => blobName;

    private static int TryFindEnd(this string allText, int start, ref int end, int length)
    {
        var lastWord = -1;
        end = start + MaxSectionLength;

        if (end > length)
        {
            end = length;
        }
        else
        {
            // Try to find the end of the sentence
            while (end < length && (end - start - MaxSectionLength) < SentenceSearchLimit && !s_sentenceEndings.Contains(allText[end]))
            {
                if (s_wordBreaks.Contains(allText[end]))
                {
                    lastWord = end;
                }
                end++;
            }

            if (end < length && !s_sentenceEndings.Contains(allText[end]) && lastWord > 0)
            {
                end = lastWord; // Fall back to at least keeping a whole word
            }
        }

        if (end < length)
        {
            end++;
        }


        return end;
    }

    private static int TryFindStart(this string allText, ref int start, int end)
    {
        var lastWord = -1;
        while (start > 0 && start > end - MaxSectionLength -
            (2 * SentenceSearchLimit) && !s_sentenceEndings.Contains(allText[start]))
        {
            if (s_wordBreaks.Contains(allText[start]))
            {
                lastWord = start;
            }
            start--;
        }

        if (!s_sentenceEndings.Contains(allText[start]) && lastWord > 0)
        {
            start = lastWord;
        }
        if (start > 0)
        {
            start++;
        }


        return start;
    }

    #endregion
}
