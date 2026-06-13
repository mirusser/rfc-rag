using RfcRag.Models;
using RfcRag.Search;

namespace RfcRag.Tests.Fakes;

internal sealed class FakeSearchService : ISearchService
{
    public sealed class SectionTree
    {
        public RfcSection Parent { get; set; } = new();
        public IReadOnlyList<RfcSection> Children { get; set; } = [];
    }

    public IReadOnlyList<SearchResult> SearchResults { get; set; } = [];
    public IReadOnlyList<RfcSection> RfcSections { get; set; } = [];
    public RfcSection? SingleSection { get; set; }
    public IReadOnlyDictionary<(int RfcNumber, string Section), RfcSection> SectionMap { get; set; } =
        new Dictionary<(int, string), RfcSection>();
    public RfcMetadata? Metadata { get; set; }
    public IReadOnlyList<RfcMetadata> BackReferences { get; set; } = [];
    public string StatsJson { get; set; } = """{"indexedRfcs":0,"sections":0,"abnfBlocks":0,"normativeOccurrences":0,"lastIndexedAtUtc":null}""";
    public IReadOnlyList<RfcMetadata> IndexedRfcList { get; set; } = [];
    public IReadOnlyDictionary<string, string?> TocMap { get; set; } = new Dictionary<string, string?>();
    public SectionTree? SectionWithChildren { get; set; }
    public IReadOnlyDictionary<string, RfcSection> ExpandedTypes { get; set; } = new Dictionary<string, RfcSection>();
    public IReadOnlyDictionary<int, RfcRelationsBatch> RelationsBatch { get; set; } =
        new Dictionary<int, RfcRelationsBatch>();
    public IReadOnlyDictionary<Guid, IReadOnlyList<NormativeOccurrenceData>> NormativeOccurrencesBatch { get; set; } =
        new Dictionary<Guid, IReadOnlyList<NormativeOccurrenceData>>();
    public IReadOnlyDictionary<string, IReadOnlyList<RfcErratum>> ErrataBatch { get; set; } =
        new Dictionary<string, IReadOnlyList<RfcErratum>>();
    public IReadOnlyList<string> LastErrataStatuses { get; private set; } = [];
    public Exception? SearchException { get; set; }
    public Exception? SearchNormativeException { get; set; }
    public Exception? SearchAbnfException { get; set; }
    public string? LastNormativeKeyword { get; private set; }
    public bool LastIncludeObsolete { get; private set; }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int limit, string? normativeKeyword, bool includeObsolete, CancellationToken cancellationToken)
    {
        LastNormativeKeyword = normativeKeyword;
        LastIncludeObsolete = includeObsolete;
        if (SearchException is not null)
            throw SearchException;
        return Task.FromResult((IReadOnlyList<SearchResult>)SearchResults.Take(limit).ToArray());
    }

    public Task<RfcSection?> GetSectionAsync(
        int rfcNumber, string section, CancellationToken cancellationToken)
    {
        if (SectionMap.TryGetValue((rfcNumber, section), out var mapped))
            return Task.FromResult<RfcSection?>(mapped);
        return Task.FromResult(SingleSection);
    }

    public Task<IReadOnlyList<RfcSection>> GetRfcAsync(
        int rfcNumber, CancellationToken cancellationToken) =>
        Task.FromResult(RfcSections);

    public Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        string keyword, int[]? rfcNumbers, int limit, CancellationToken cancellationToken)
    {
        if (SearchNormativeException is not null)
            throw SearchNormativeException;
        return Task.FromResult(SearchResults);
    }

    public Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        string query, int[]? rfcNumbers, int limit, CancellationToken cancellationToken)
    {
        if (SearchAbnfException is not null)
            throw SearchAbnfException;
        return Task.FromResult(SearchResults);
    }

    public Task<RfcMetadata?> GetRfcMetadataAsync(
        int rfcNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Metadata);

    public Task<IReadOnlyList<RfcMetadata>> FindBackReferencesAsync(
        int rfcNumber, CancellationToken cancellationToken) =>
        Task.FromResult(BackReferences);

    public Task<IReadOnlyList<RfcMetadata>> ListIndexedAsync(
        int limit, int offset, CancellationToken cancellationToken) =>
        Task.FromResult(IndexedRfcList);

    public Task<string> GetStatsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(StatsJson);

    public Task<IReadOnlyDictionary<string, string?>> GetTocAsync(
        int rfcNumber, CancellationToken cancellationToken) =>
        Task.FromResult(TocMap);

    public Task<(RfcSection Parent, IReadOnlyList<RfcSection> Children)> GetSectionWithChildrenAsync(
        int rfcNumber, string section, int depth, CancellationToken cancellationToken)
    {
        if (SectionWithChildren is null)
            return Task.FromResult((new RfcSection(), (IReadOnlyList<RfcSection>)[]));
        return Task.FromResult((SectionWithChildren.Parent, SectionWithChildren.Children));
    }

    public Task<IReadOnlyDictionary<string, RfcSection>> GetSectionWithExpandedTypesAsync(
        int rfcNumber, string section, CancellationToken cancellationToken) =>
        Task.FromResult(ExpandedTypes);

    public Task<IReadOnlyDictionary<int, RfcRelationsBatch>> GetRelationsBatchAsync(
        IReadOnlyList<int> rfcNumbers, CancellationToken cancellationToken) =>
        Task.FromResult(RelationsBatch);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<NormativeOccurrenceData>>> GetNormativeOccurrencesBatchAsync(
        IReadOnlyList<Guid> sectionIds, CancellationToken cancellationToken) =>
        Task.FromResult(NormativeOccurrencesBatch);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<RfcErratum>>> GetErrataBatchAsync(
        IReadOnlyList<int> rfcNumbers,
        IReadOnlyCollection<string> statuses,
        CancellationToken cancellationToken)
    {
        LastErrataStatuses = statuses.ToArray();
        return Task.FromResult(ErrataBatch);
    }
}
