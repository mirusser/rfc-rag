namespace RfcRag.Search;

internal sealed record class HybridCandidate(
    Guid Id,
    int RfcNumber,
    string Title,
    string Section,
    string? Heading,
    string Excerpt,
    string SourcePath,
    string Url,
    long LexicalRank,
    long VectorRank,
    double RrfScore);
