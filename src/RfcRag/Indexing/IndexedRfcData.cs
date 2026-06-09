namespace RfcRag.Indexing;

/// <summary>
/// Bundled data for upserting an indexed RFC record.
/// Introduced to reduce the parameter count of
/// <see cref="IndexingRepository.UpsertIndexedRfcAsync"/>.
/// </summary>
public sealed record class IndexedRfcData(
    int RfcNumber,
    string SourcePath,
    string SourceSha256,
    string Title,
    int SectionCount,
    int[] Updates,
    int[] Obsoletes,
    string? Date,
    string? Category,
    string[] Authors,
    string? Issn,
    string GrammarStyle);
