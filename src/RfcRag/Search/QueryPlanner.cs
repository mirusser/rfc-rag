using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RfcRag.Search;

internal static partial class QueryPlanner
{
    private static readonly FrozenDictionary<string, int[]> ProtocolSeedRfcNumbers =
        new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["DNS"] = [1034, 1035, 6891, 8499],
            ["HTTP"] = [9110, 9111, 9112, 9113, 9114],
            ["JWT"] = [7519, 8725],
            ["OAuth"] = [6749, 6750, 8414, 8628, 8693, 8705, 9068],
            ["QUIC"] = [9000, 9001, 9002],
            ["SMTP"] = [5321, 5322, 6531],
            ["TCP"] = [9293],
            ["TLS"] = [8446],
            ["URI"] = [3986, 3987],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static QueryPlan Plan(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        int[] rfcNumbers = RfcNumberRegex()
            .Matches(query)
            .Select(match => int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();

        QuerySectionReference[] sectionReferences = RfcSectionReferenceRegex()
            .Matches(query)
            .Select(match => new QuerySectionReference(
                int.Parse(match.Groups["rfc"].Value, CultureInfo.InvariantCulture),
                match.Groups["section"].Value))
            .Distinct()
            .ToArray();

        string? suggestedNormativeKeyword = GetSuggestedNormativeKeyword(query);
        int[] protocolRfcNumbers = GetProtocolRfcNumbers(query);
        bool hasAbnfIntent = AbnfIntentRegex().IsMatch(query);
        bool includeObsolete = HistoricalIntentRegex().IsMatch(query);

        var rationale = new List<string>();
        if (rfcNumbers.Length > 0)
            rationale.Add(QueryPlanRationale.RfcNumber);
        if (sectionReferences.Length > 0)
            rationale.Add(QueryPlanRationale.SectionReference);
        if (suggestedNormativeKeyword is not null)
            rationale.Add(QueryPlanRationale.NormativeKeyword);
        if (protocolRfcNumbers.Length > 0)
            rationale.Add(QueryPlanRationale.ProtocolHint);
        if (hasAbnfIntent)
            rationale.Add(QueryPlanRationale.AbnfIntent);
        if (includeObsolete)
            rationale.Add(QueryPlanRationale.HistoricalIntent);

        return new QueryPlan(
            rfcNumbers,
            sectionReferences,
            protocolRfcNumbers,
            suggestedNormativeKeyword,
            hasAbnfIntent,
            includeObsolete,
            NeedsCurrentSpec: !includeObsolete,
            rationale);
    }

    private static string? GetSuggestedNormativeKeyword(string query)
    {
        if (UppercaseNormativeKeywordTopicRegex().IsMatch(query))
            return null;

        if (MustNotIntentRegex().IsMatch(query))
            return QueryPlanNormativeKeywords.MustNot;

        if (MustIntentRegex().IsMatch(query))
            return QueryPlanNormativeKeywords.Must;

        return AllowedIntentRegex().IsMatch(query) ? QueryPlanNormativeKeywords.May : null;
    }

    private static int[] GetProtocolRfcNumbers(string query)
    {
        return ProtocolHintRegex()
            .Matches(query)
            .SelectMany(match =>
                ProtocolSeedRfcNumbers.TryGetValue(match.Value, out int[]? rfcNumbers)
                    ? rfcNumbers
                    : [])
            .Distinct()
            .Order()
            .ToArray();
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex RfcNumberRegexInstance =
        CreateRegex(@"\bRFC\s*(?<number>\d{3,5})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RfcSectionReferenceRegexInstance = CreateRegex(
        @"\bRFC\s*(?<rfc>\d{3,5})\b.{0,80}?(?:\bsection\s+|§\s*)(?<section>\d+(?:\.\d+)*[A-Za-z]?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex MustNotIntentRegexInstance =
        CreateRegex(@"\b(forbidden|must\s+not)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MustIntentRegexInstance =
        CreateRegex(@"\b(must|required|compliant)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AllowedIntentRegexInstance =
        CreateRegex(@"\ballowed\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ProtocolHintRegexInstance =
        CreateRegex(@"\b(HTTP|TLS|OAuth|JWT|DNS|SMTP|QUIC|URI|TCP)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UppercaseNormativeKeywordTopicRegexInstance = CreateRegex(
        @"\b(MUST NOT|MUST|REQUIRED|SHALL NOT|SHALL|SHOULD NOT|SHOULD|NOT RECOMMENDED|RECOMMENDED|MAY|OPTIONAL)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex AbnfIntentRegexInstance =
        CreateRegex(@"\b(ABNF|grammar|syntax|Augmented\s+BNF)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HistoricalIntentRegexInstance =
        CreateRegex(@"\b(old|obsolete|changed\s+from)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex CreateRegex(string pattern, RegexOptions options) => new(pattern, options, RegexTimeout);

    private static Regex RfcNumberRegex() => RfcNumberRegexInstance;

    private static Regex RfcSectionReferenceRegex() => RfcSectionReferenceRegexInstance;

    private static Regex MustNotIntentRegex() => MustNotIntentRegexInstance;

    private static Regex MustIntentRegex() => MustIntentRegexInstance;

    private static Regex AllowedIntentRegex() => AllowedIntentRegexInstance;

    private static Regex ProtocolHintRegex() => ProtocolHintRegexInstance;

    private static Regex UppercaseNormativeKeywordTopicRegex() => UppercaseNormativeKeywordTopicRegexInstance;

    private static Regex AbnfIntentRegex() => AbnfIntentRegexInstance;

    private static Regex HistoricalIntentRegex() => HistoricalIntentRegexInstance;
}

internal static class QueryPlanRationale
{
    public const string RfcNumber = "rfc-number";
    public const string SectionReference = "section-reference";
    public const string NormativeKeyword = "normative-keyword";
    public const string ProtocolHint = "protocol-hint";
    public const string AbnfIntent = "abnf-intent";
    public const string HistoricalIntent = "historical-intent";
}

internal static class QueryPlanNormativeKeywords
{
    public const string Must = "MUST";
    public const string MustNot = "MUST NOT";
    public const string May = "MAY";
}
