using RfcRag.Answering;
using RfcRag.Models;
using RfcRag.Search;
using RfcRag.Tests.Fakes;

namespace RfcRag.Tests.UnitTests;

public sealed class ContextAssemblerTests
{
    private static RfcSection MakeSection(int rfcNumber, string section, string? heading, string text)
    {
        return new RfcSection
        {
            RfcNumber = rfcNumber,
            Section = section,
            Heading = heading,
            Text = text,
            Title = $"RFC {rfcNumber}",
            SourcePath = $"/rfc{rfcNumber}.txt",
            Url = $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}",
        };
    }

    private static SearchResult MakeResult(int rfcNumber, string section, string? heading, string excerpt, double score)
    {
        return new SearchResult(
            Guid.NewGuid(),
            rfcNumber,
            $"RFC {rfcNumber}",
            section,
            heading,
            excerpt,
            $"/rfc{rfcNumber}.txt",
            $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}",
            score);
    }

    [Fact]
    public async Task AssembleAsync_EmptyResults_ReturnsEmptyPack()
    {
        var fakeService = new FakeSearchService();
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [], budgetChars: 5000, CancellationToken.None);

        Assert.Empty(pack.Sections);
        Assert.Equal(0, pack.TotalChars);
        Assert.Equal(5000, pack.BudgetChars);
        Assert.False(pack.BudgetExceeded);
        Assert.Equal("test query", pack.Query);
    }

    [Fact]
    public async Task AssembleAsync_SingleResult_FetchesFullSectionAndBuildsEvidence()
    {
        var section = MakeSection(9110, "1", "Introduction", "This is the full text of the introduction.");
        var result = MakeResult(9110, "1", "Introduction", "This is the full...", 0.95);
        var toc = new Dictionary<string, string?> { ["1"] = "Introduction" };

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection> { [(9110, "1")] = section },
            TocMap = toc,
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [result], budgetChars: 5000, CancellationToken.None);

        Assert.Single(pack.Sections);
        var evidence = pack.Sections[0];
        Assert.Equal(9110, evidence.RfcNumber);
        Assert.Equal("1", evidence.Section);
        Assert.Equal("Introduction", evidence.Heading);
        Assert.Equal("9110#1", evidence.EvidenceId);
        Assert.Equal("This is the full text of the introduction.", evidence.Text);
        Assert.Equal(0.95, evidence.Score);
        Assert.Equal(42, pack.TotalChars);
        Assert.False(pack.BudgetExceeded);
    }

    [Fact]
    public async Task AssembleAsync_DuplicateSections_DeduplicatesKeepingHighestScore()
    {
        var section = MakeSection(9110, "1", "Intro", "Full text.");
        var result1 = MakeResult(9110, "1", "Intro", "excerpt", 0.95); // higher score
        var result2 = MakeResult(9110, "1", "Intro", "excerpt", 0.80); // lower score - should be dropped

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection> { [(9110, "1")] = section },
            TocMap = new Dictionary<string, string?> { ["1"] = "Intro" },
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [result1, result2], budgetChars: 5000, CancellationToken.None);

        Assert.Single(pack.Sections);
        Assert.Equal(0.95, pack.Sections[0].Score);
    }

    [Fact]
    public async Task AssembleAsync_AncestorDescendantOverlap_CollapsesToMoreSpecific()
    {
        // Both 3.7 (parent) and 3.7.1 (child) appear — keep the more specific child
        var parent = MakeSection(9110, "3.7", "Header Fields", "Parent text about header fields.");
        var child = MakeSection(9110, "3.7.1", "Field Names", "Child text about field names specifically.");
        var resultParent = MakeResult(9110, "3.7", "Header Fields", "Parent...", 0.90);
        var resultChild = MakeResult(9110, "3.7.1", "Field Names", "Child...", 0.85);

        var toc = new Dictionary<string, string?>
        {
            ["3"] = "Message Format",
            ["3.7"] = "Header Fields",
            ["3.7.1"] = "Field Names",
        };

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection>
            {
                [(9110, "3.7")] = parent,
                [(9110, "3.7.1")] = child,
            },
            TocMap = toc,
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [resultChild, resultParent], budgetChars: 5000, CancellationToken.None);

        // The child should be kept; parent collapsed
        Assert.Single(pack.Sections);
        Assert.Equal("3.7.1", pack.Sections[0].Section);

        // Should have a warning about overlap
        Assert.Contains(pack.Warnings, w => w.Type == "overlap_collapsed");
    }

    [Fact]
    public async Task AssembleAsync_BudgetExceeded_TruncatesAndWarns()
    {
        var section1 = MakeSection(9110, "1", "Intro", new string('a', 100));
        var section2 = MakeSection(9110, "2", "Overview", new string('b', 200));
        var result1 = MakeResult(9110, "1", "Intro", "a...", 0.95);
        var result2 = MakeResult(9110, "2", "Overview", "b...", 0.85);

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection>
            {
                [(9110, "1")] = section1,
                [(9110, "2")] = section2,
            },
            TocMap = new Dictionary<string, string?>
            {
                ["1"] = "Intro",
                ["2"] = "Overview",
            },
        };
        var assembler = new ContextAssembler(fakeService);

        // Budget allows section1 (100 chars) but not section2 (200 chars)
        var pack = await assembler.AssembleAsync(
            "test query", [result1, result2], budgetChars: 150, CancellationToken.None);

        Assert.True(pack.BudgetExceeded);
        Assert.Contains(pack.Warnings, w => w.Type == "budget_exceeded");

        // First section fits, second omitted
        Assert.Single(pack.Sections);
        Assert.Equal("1", pack.Sections[0].Section);
    }

    [Fact]
    public async Task AssembleAsync_SingleOversizedSection_KeepsItWithWarning()
    {
        var bigSection = MakeSection(9110, "1", "Intro", new string('x', 300));
        var result = MakeResult(9110, "1", "Intro", "x...", 0.95);

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection> { [(9110, "1")] = bigSection },
            TocMap = new Dictionary<string, string?> { ["1"] = "Intro" },
        };
        var assembler = new ContextAssembler(fakeService);

        // Budget is smaller than the single section
        var pack = await assembler.AssembleAsync(
            "test query", [result], budgetChars: 100, CancellationToken.None);

        // Must include at least the top section even if oversized
        Assert.Single(pack.Sections);
        Assert.Equal(300, pack.TotalChars);
        Assert.True(pack.BudgetExceeded);
        Assert.Contains(pack.Warnings, w => w.Type == "budget_exceeded");
    }

    [Fact]
    public async Task AssembleAsync_MultiRfc_AttachesParentHeadings()
    {
        var section = MakeSection(9110, "9.3.1", "GET", "The GET method...");
        var result = MakeResult(9110, "9.3.1", "GET", "The GET...", 0.95);

        var toc = new Dictionary<string, string?>
        {
            ["9"] = "Methods",
            ["9.3"] = "Request Methods",
            ["9.3.1"] = "GET",
        };

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection> { [(9110, "9.3.1")] = section },
            TocMap = toc,
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [result], budgetChars: 5000, CancellationToken.None);

        Assert.Single(pack.Sections);
        var evidence = pack.Sections[0];
        Assert.Equal(2, evidence.ParentHeadings.Count);
        Assert.Equal("Methods", evidence.ParentHeadings[0]);
        Assert.Equal("Request Methods", evidence.ParentHeadings[1]);
    }

    [Fact]
    public async Task AssembleAsync_DeterministicOrdering_ByScoreThenSection()
    {
        var s1 = MakeSection(9110, "1", "A", "a");
        var s2 = MakeSection(8446, "4", "B", "b");
        var r1 = MakeResult(9110, "1", "A", "a", 0.50);
        var r2 = MakeResult(8446, "4", "B", "b", 0.99); // higher score, should appear first

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection>
            {
                [(9110, "1")] = s1,
                [(8446, "4")] = s2,
            },
            TocMap = new Dictionary<string, string?>
            {
                ["1"] = "A",
                ["4"] = "B",
            },
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [r1, r2], budgetChars: 5000, CancellationToken.None);

        Assert.Equal(2, pack.Sections.Count);
        Assert.Equal(8446, pack.Sections[0].RfcNumber); // higher score first
        Assert.Equal(9110, pack.Sections[1].RfcNumber);
    }

    [Fact]
    public async Task AssembleAsync_PerRfcCap_LimitsSectionsPerRfc()
    {
        // Create 6 results from the same RFC (cap is 5)
        var sections = new Dictionary<(int, string), RfcSection>();
        var results = new List<SearchResult>();
        for (int i = 1; i <= 6; i++)
        {
            var secNum = i.ToString();
            var section = MakeSection(9110, secNum, $"Section {i}", new string((char)('a' + i - 1), 10));
            sections[(9110, secNum)] = section;
            results.Add(MakeResult(9110, secNum, $"Section {i}", "excerpt", 1.0 - i * 0.01));
        }

        var toc = Enumerable.Range(1, 6).ToDictionary(i => i.ToString(), i => (string?)$"Section {i}");

        var fakeService = new FakeSearchService
        {
            SectionMap = sections,
            TocMap = toc,
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", results, budgetChars: 5000, CancellationToken.None);

        // Only 5 sections should be included (cap per RFC)
        Assert.Equal(5, pack.Sections.Count);
        // All sections should be from RFC 9110
        Assert.All(pack.Sections, s => Assert.Equal(9110, s.RfcNumber));
    }

    [Fact]
    public async Task AssembleAsync_Enrichment_PopulatesRelationNotes()
    {
        var section = MakeSection(9110, "1", "Intro", "Full text.");
        var result = MakeResult(9110, "1", "Intro", "excerpt", 0.95);

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection> { [(9110, "1")] = section },
            TocMap = new Dictionary<string, string?> { ["1"] = "Intro" },
            RelationsBatch = new Dictionary<int, RfcRelationsBatch>
            {
                [9110] = new RfcRelationsBatch
                {
                    RfcNumber = 9110,
                    ObsoletedBy = [9112],
                },
            },
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [result], budgetChars: 5000, CancellationToken.None);

        Assert.Single(pack.RelationNotes);
        Assert.Contains("RFC 9110 is obsoleted by RFC 9112.", pack.RelationNotes);
    }

    [Fact]
    public async Task AssembleAsync_Enrichment_AddsObsoletedRfcWarning()
    {
        var section = MakeSection(9110, "1", "Intro", "Full text.");
        var result = MakeResult(9110, "1", "Intro", "excerpt", 0.95);

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection> { [(9110, "1")] = section },
            TocMap = new Dictionary<string, string?> { ["1"] = "Intro" },
            RelationsBatch = new Dictionary<int, RfcRelationsBatch>
            {
                [9110] = new RfcRelationsBatch
                {
                    RfcNumber = 9110,
                    ObsoletedBy = [9112, 9113],
                },
            },
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [result], budgetChars: 5000, CancellationToken.None);

        var warning = Assert.Single(pack.Warnings, w => w.Type == "obsoleted_rfc");
        Assert.Contains("RFC 9110 is obsoleted by RFC 9112, 9113", warning.Message);
    }

    [Fact]
    public async Task AssembleAsync_EstimatedTokens_CalculatedCorrectly()
    {
        // Text with 100 chars → estimated tokens = 100 / 4 = 25
        var section1 = MakeSection(9110, "1", "Intro", new string('a', 100));
        var result1 = MakeResult(9110, "1", "Intro", "a...", 0.95);

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection> { [(9110, "1")] = section1 },
            TocMap = new Dictionary<string, string?> { ["1"] = "Intro" },
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [result1], budgetChars: 5000, CancellationToken.None);

        Assert.Equal(100, pack.TotalChars);
        Assert.Equal(25, pack.EstimatedTokens); // 100 / 4
    }

    [Fact]
    public async Task AssembleAsync_TieBreakOrdering_SameScoreOrderedByRfcThenSection()
    {
        // Three results with same score — should be ordered by RfcNumber ascending, then section
        var s1 = MakeSection(8446, "4.2", "ServerHello", "TLS 1.3 handshake.");
        var s2 = MakeSection(9110, "1", "Intro", "HTTP intro.");
        var s3 = MakeSection(8446, "1", "Intro", "TLS intro.");

        // Same score for all three
        var r1 = MakeResult(8446, "4.2", "ServerHello", "TLS...", 0.50);
        var r2 = MakeResult(9110, "1", "Intro", "HTTP...", 0.50);
        var r3 = MakeResult(8446, "1", "Intro", "TLS...", 0.50);

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection>
            {
                [(8446, "4.2")] = s1,
                [(9110, "1")] = s2,
                [(8446, "1")] = s3,
            },
            TocMap = new Dictionary<string, string?>
            {
                ["1"] = "Intro",
                ["4.2"] = "ServerHello",
            },
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [r1, r2, r3], budgetChars: 5000, CancellationToken.None);

        // Same score → ordered by RfcNumber then Section
        // 8446#1 (section "1" comes before "4.2"), then 8446#4.2, then 9110#1
        Assert.Equal(3, pack.Sections.Count);
        Assert.Equal("8446#1", pack.Sections[0].EvidenceId);
        Assert.Equal("8446#4.2", pack.Sections[1].EvidenceId);
        Assert.Equal("9110#1", pack.Sections[2].EvidenceId);
    }

    [Fact]
    public async Task AssembleAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var fakeService = new FakeSearchService();
        var assembler = new ContextAssembler(fakeService);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            assembler.AssembleAsync("query", [], 5000, cts.Token));
    }

    [Fact]
    public async Task AssembleAsync_Enrichment_PopulatesNormativeOccurrences()
    {
        var sectionId = Guid.NewGuid();
        var section = MakeSection(9110, "1", "Intro", "Full text about MUST requirements.");
        var result = new SearchResult(
            sectionId, 9110, "HTTP Semantics", "1", "Intro", "excerpt",
            "/rfc9110.txt", "https://example.com", 0.95);

        var occurrences = new List<NormativeOccurrenceData>
        {
            new() { Keyword = "MUST", LineOffset = 0 },
            new() { Keyword = "SHOULD", LineOffset = 2 },
        };

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection> { [(9110, "1")] = section },
            TocMap = new Dictionary<string, string?> { ["1"] = "Intro" },
            NormativeOccurrencesBatch = new Dictionary<Guid, IReadOnlyList<NormativeOccurrenceData>>
            {
                [sectionId] = occurrences,
            },
        };
        var assembler = new ContextAssembler(fakeService);

        var pack = await assembler.AssembleAsync(
            "test query", [result], budgetChars: 5000, CancellationToken.None);

        var evidence = Assert.Single(pack.Sections);
        Assert.NotNull(evidence.NormativeOccurrences);
        Assert.Equal(2, evidence.NormativeOccurrences.Count);
        Assert.Contains(evidence.NormativeOccurrences, n => n.Keyword == "MUST");
        Assert.Contains(evidence.NormativeOccurrences, n => n.Keyword == "SHOULD");
    }

    [Fact]
    public async Task AssembleAsync_MidExecutionCancellation_ThrowsOperationCanceledException()
    {
        var section1 = MakeSection(9110, "1", "Intro", new string('a', 100));
        var section2 = MakeSection(9110, "2", "Overview", new string('b', 200));
        var result1 = MakeResult(9110, "1", "Intro", "a...", 0.95);
        var result2 = MakeResult(9110, "2", "Overview", "b...", 0.85);

        var fakeService = new FakeSearchService
        {
            SectionMap = new Dictionary<(int, string), RfcSection>
            {
                [(9110, "1")] = section1,
                [(9110, "2")] = section2,
            },
            TocMap = new Dictionary<string, string?>
            {
                ["1"] = "Intro",
                ["2"] = "Overview",
            },
        };
        var assembler = new ContextAssembler(fakeService);

        using var cts = new CancellationTokenSource();
        // Pre-cancel before first section fetch to exercise the loop's cancellation check
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            assembler.AssembleAsync("query", [result1, result2], 5000, cts.Token));
    }
}
