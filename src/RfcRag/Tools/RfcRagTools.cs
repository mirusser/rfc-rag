using System.ComponentModel;
using System.Text.Json;
using RfcRag.Search;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RfcRag.Tools;

[McpServerToolType]
[Description("RFC RAG search and retrieval tools")]
public static class RfcRagTools
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool(Name = "search_rfc", ReadOnly = true, OpenWorld = false)]
    [Description("Search RFCs using hybrid vector + full-text search. Returns ranked sections with excerpts and RFC status.")]
    public static async Task<CallToolResult> SearchRfc(
        ISearchService search,
        [Description("Search query for RFC content.")] string query,
        [Description("Maximum ranked sections to return, from 1 to 100.")] int limit = 10,
        [Description("Optional normative keyword filter (e.g., 'MUST NOT', 'SHOULD', 'REQUIRED'). When set, only sections containing this RFC 2119/8174 keyword are returned.")] string? normative_keyword = null,
        [Description("When true, includes obsoleted RFCs without penalty or warning. Default false demotes and flags obsoleted RFCs.")] bool include_obsolete = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SearchResult> results = await search.SearchAsync(query, limit, normative_keyword, include_obsolete, cancellationToken).ConfigureAwait(false);
        return JsonResult(results);
    }

    [McpServerTool(Name = "get_rfc", ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve RFC metadata, table of contents, and a preview of the first sections.")]
    public static async Task<CallToolResult> GetRfc(
        ISearchService search,
        [Description("RFC number to retrieve.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RfcSection> sections = await search.GetRfcAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        if (sections.Count == 0)
        {
            return ErrorResult($"RFC {rfcNumber} is not indexed.");
        }

        const int previewLimit = 20;
        var toc = sections.ToDictionary(s => s.Section, s => s.Heading, StringComparer.Ordinal);
        IReadOnlyList<RfcSection> previewSections = sections.Take(previewLimit).ToArray();

        return JsonResult(new
        {
            rfcNumber,
            title = sections[0].Title,
            sourcePath = sections[0].SourcePath,
            url = sections[0].Url,
            sectionCount = sections.Count,
            toc,
            sections = previewSections
        });
    }

    [McpServerTool(Name = "get_rfc_full", ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve the full concatenated text of an RFC. Use sparingly — output can be very large.")]
    public static async Task<CallToolResult> GetRfcFull(
        ISearchService search,
        [Description("RFC number to retrieve.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RfcSection> sections = await search.GetRfcAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        if (sections.Count == 0)
        {
            return ErrorResult($"RFC {rfcNumber} is not indexed.");
        }

        string fullText = string.Join("\n\n", sections.Select(s => s.Text));

        return JsonResult(new
        {
            rfcNumber,
            title = sections[0].Title,
            sourcePath = sections[0].SourcePath,
            url = sections[0].Url,
            sectionCount = sections.Count,
            text = fullText
        });
    }

    [McpServerTool(Name = "get_rfc_section", ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve a specific section of an RFC. Example section: '6.3'. Set depth=1 to include child sections (when depth>0, expand is ignored). Set expand=true to resolve type references when depth=0.")]
    public static async Task<CallToolResult> GetRfcSection(
        ISearchService search,
        [Description("RFC number to retrieve from.")] int rfcNumber,
        [Description("Section number to retrieve, for example 6.3.")] string section,
        [Description("Number of child levels to include (0 = section only, 1 = immediate children).")] int depth = 0,
        [Description("When true, resolves PascalCase type references to their defining sections.")] bool expand = false,
        CancellationToken cancellationToken = default)
    {
        if (depth > 0)
        {
            (RfcSection parent, IReadOnlyList<RfcSection> children) = await search.GetSectionWithChildrenAsync(
                rfcNumber, section, depth, cancellationToken).ConfigureAwait(false);

            if (parent.Section.Length == 0)
                return ErrorResult($"Section {section} of RFC {rfcNumber} is not indexed.");

            return JsonResult(new { section = parent, children });
        }

        RfcSection? result = await search.GetSectionAsync(rfcNumber, section, cancellationToken).ConfigureAwait(false);
        if (result is null)
            return ErrorResult($"Section {section} of RFC {rfcNumber} is not indexed.");

        if (expand)
        {
            IReadOnlyDictionary<string, RfcSection> expandedTypes = await search.GetSectionWithExpandedTypesAsync(
                rfcNumber, section, cancellationToken).ConfigureAwait(false);

            return expandedTypes.Count > 0
                ? JsonResult(new { section = result, expandedTypes })
                : JsonResult(result);
        }

        return JsonResult(result);
    }

    [McpServerTool(Name = "get_rfc_metadata", ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve metadata for a specific RFC (title, updates, obsoletes).")]
    public static async Task<CallToolResult> GetRfcMetadata(
        ISearchService search,
        [Description("RFC number to retrieve metadata for.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        RfcMetadata? metadata = await search.GetRfcMetadataAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        return metadata is null
            ? ErrorResult($"RFC {rfcNumber} is not indexed.")
            : JsonResult(metadata);
    }

    [McpServerTool(Name = "list_indexed_rfcs", ReadOnly = true, OpenWorld = false)]
    [Description("List indexed RFCs with their numbers and titles.")]
    public static async Task<CallToolResult> ListIndexedRfcs(
        ISearchService search,
        [Description("Maximum results to return, from 1 to 1000.")] int limit = 100,
        [Description("Number of results to skip for pagination.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RfcMetadata> rfcs = await search.ListIndexedAsync(limit, offset, cancellationToken).ConfigureAwait(false);
        return JsonResult(new { total = rfcs.Count, rfcs });
    }

    [McpServerTool(Name = "search_normative", ReadOnly = true, OpenWorld = false)]
    [Description("Search for normative keywords (MUST, SHOULD, MAY, etc.) in RFCs. Keyword must be one of: MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY, OPTIONAL.")]
    public static async Task<CallToolResult> SearchNormative(
        ISearchService search,
        [Description("Normative keyword to search for.")] string keyword,
        [Description("Optional RFC number filter.")] int[]? rfcNumbers = null,
        [Description("Maximum results to return, from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SearchResult> results = await search.SearchNormativeAsync(
            keyword,
            rfcNumbers,
            limit,
            cancellationToken).ConfigureAwait(false);
        return JsonResult(results);
    }

    [McpServerTool(Name = "search_abnf", ReadOnly = true, OpenWorld = false)]
    [Description("Search for ABNF grammar definitions by rule name or fragment.")]
    public static async Task<CallToolResult> SearchAbnf(
        ISearchService search,
        [Description("ABNF rule name or grammar fragment to search for.")] string query,
        [Description("Optional RFC number filter.")] int[]? rfcNumbers = null,
        [Description("Maximum results to return, from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SearchResult> results = await search.SearchAbnfAsync(
            query,
            rfcNumbers,
            limit,
            cancellationToken).ConfigureAwait(false);
        return JsonResult(results);
    }

    [McpServerTool(Name = "find_updates_obsoletes", ReadOnly = true, OpenWorld = false)]
    [Description("Find RFCs that update or obsolete a given RFC (back-reference lookup).")]
    public static async Task<CallToolResult> FindUpdatesObsoletes(
        ISearchService search,
        [Description("RFC number whose metadata relationships should be retrieved.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        RfcMetadata? metadata = await search.GetRfcMetadataAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return ErrorResult($"RFC {rfcNumber} is not indexed.");
        }

        IReadOnlyList<RfcMetadata> backRefs = await search.FindBackReferencesAsync(rfcNumber, cancellationToken).ConfigureAwait(false);

        return JsonResult(new
        {
            rfcNumber,
            metadata.Title,
            updates = metadata.Updates,
            obsoletes = metadata.Obsoletes,
            updated_by = backRefs
                .Where(r => r.Updates.Contains(rfcNumber))
                .Select(r => new { r.Number, r.Title })
                .ToArray(),
            obsoleted_by = backRefs
                .Where(r => r.Obsoletes.Contains(rfcNumber))
                .Select(r => new { r.Number, r.Title })
                .ToArray()
        });
    }

    [McpServerTool(Name = "rfc_stats", ReadOnly = true, OpenWorld = false)]
    [Description("Get statistics about the indexed RFC corpus, including the latest Index Manifest.")]
    public static async Task<CallToolResult> RfcStats(
        ISearchService search,
        CancellationToken cancellationToken = default)
    {
        string json = await search.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        return new CallToolResult { Content = [new TextContentBlock { Text = json }] };
    }

    [McpServerTool(Name = "get_rfc_toc", ReadOnly = true, OpenWorld = false)]
    [Description("Get the table of contents for an RFC as a flat section→heading map.")]
    public static async Task<CallToolResult> GetRfcToc(
        ISearchService search,
        [Description("RFC number to retrieve TOC for.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, string?> toc = await search.GetTocAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        if (toc.Count == 0)
        {
            return ErrorResult($"RFC {rfcNumber} is not indexed.");
        }

        return JsonResult(toc);
    }

    private static CallToolResult JsonResult<T>(T value) => new()
    {
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(value, jsonOptions) }]
    };

    private static CallToolResult ErrorResult(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }]
    };
}
