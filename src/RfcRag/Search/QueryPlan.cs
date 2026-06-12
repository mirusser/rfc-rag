namespace RfcRag.Search;

internal sealed record class QueryPlan(
    IReadOnlyList<int> RfcNumbers,
    IReadOnlyList<QuerySectionReference> SectionReferences,
    IReadOnlyList<int> ProtocolRfcNumbers,
    string? SuggestedNormativeKeyword,
    bool HasAbnfIntent,
    bool IncludeObsolete,
    bool NeedsCurrentSpec,
    IReadOnlyList<string> Rationale);

internal sealed record class QuerySectionReference(int RfcNumber, string Section);
