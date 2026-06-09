using RfcRag.Models;
using RfcRag.Parsing;

namespace RfcRag.Tests.UnitTests;

public sealed class RfcParserTests
{
    private readonly RfcParser parser = new();
    private readonly RfcXmlParser xmlParser = new();

    [Fact]
    public async Task ParseAsync_RealRfc2119_ExtractsCorrectMetadata()
    {
        string fixturePath = Path.Combine("TestData", "rfc2119.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.NotNull(document);
        Assert.NotNull(document.Metadata);
        Assert.Equal(2119, document.Metadata.Number);
        Assert.Contains("Key words", document.Metadata.Title);
        Assert.NotEmpty(document.Sections);
    }

    [Fact]
    public async Task ParseAsync_RealRfc2119_ExtractsSections()
    {
        string fixturePath = Path.Combine("TestData", "rfc2119.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.NotEmpty(document.Sections);
        RfcSection? introSection = document.Sections.FirstOrDefault(s => s.Section == "1");
        Assert.NotNull(introSection);
        Assert.Contains("MUST", introSection!.Text);
        Assert.Contains("rfc2119.txt", introSection.SourcePath);
        Assert.Contains("rfc-editor.org", introSection.Url);
    }

    [Fact]
    public async Task ParseAsync_RealRfc2119_ExtractsNormativeKeywords()
    {
        string fixturePath = Path.Combine("TestData", "rfc2119.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        var mustOccurrences = document.NormativeOccurrences
            .Where(n => n.Keyword == "MUST")
            .ToList();
        Assert.NotEmpty(mustOccurrences);

        var shouldOccurrences = document.NormativeOccurrences
            .Where(n => n.Keyword == "SHOULD")
            .ToList();
        Assert.NotEmpty(shouldOccurrences);
    }

    [Fact]
    public async Task ParseAsync_RealRfc9110_ExtractsComplexSections()
    {
        string fixturePath = Path.Combine("TestData", "rfc9110.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(9110, document.Metadata.Number);
        Assert.Contains("HTTP Semantics", document.Metadata.Title);
        Assert.True(document.Sections.Count > 5, $"Expected >5 sections, got {document.Sections.Count}");

        RfcSection? section63 = document.Sections.FirstOrDefault(s => s.Section == "6.3");
        Assert.NotNull(section63);
    }

    [Fact]
    public async Task ParseAsync_RealRfc8446_ExtractsMultipleSections()
    {
        string fixturePath = Path.Combine("TestData", "rfc8446.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(8446, document.Metadata.Number);
        Assert.Contains("TLS", document.Metadata.Title);
        Assert.True(document.Sections.Count > 3, $"Expected >3 sections, got {document.Sections.Count}");
    }

    [Fact]
    public async Task ParseAsync_RealRfc9110_ExtractsAbnfBlocks()
    {
        string fixturePath = Path.Combine("TestData", "rfc9110.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.NotEmpty(document.AbnfBlocks);
    }

    [Fact]
    public async Task ParseAsync_InvalidFilename_ThrowsFormatException()
    {
        string fixturePath = Path.Combine("TestData", "badfile.txt");
        await Assert.ThrowsAsync<FormatException>(() => parser.ParseAsync(fixturePath, CancellationToken.None));
    }

    [Fact]
    public async Task NormativeKeywords_Dedup_DoesNotDoubleCountMustNotAsMust()
    {
        string fixturePath = Path.Combine("TestData", "rfc2119.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        var mustNot = document.NormativeOccurrences.Where(n => n.Keyword == "MUST NOT").ToList();
        var must = document.NormativeOccurrences.Where(n => n.Keyword == "MUST").ToList();

        Assert.NotEmpty(mustNot);
        Assert.NotEmpty(must);

        foreach (NormativeOccurrence mn in mustNot)
        {
            bool hasOverlappingMust = must.Any(m =>
                m.SectionId == mn.SectionId && m.LineOffset == mn.LineOffset);
            Assert.False(hasOverlappingMust,
                $"MUST NOT at section {mn.SectionId} line {mn.LineOffset} also counted as MUST");
        }
    }

    [Fact]
    public async Task AbnfBlocks_RealRfc9110_IncludesMultiLineDefinitions()
    {
        string fixturePath = Path.Combine("TestData", "rfc9110.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.NotEmpty(document.AbnfBlocks);

        bool hasMultiLineBlock = document.AbnfBlocks.Any(b => b.AbnfText.Contains('\n'));
        Assert.True(hasMultiLineBlock, "Expected at least one ABNF block with 3+ lines");
    }

    [Fact]
    public async Task ParseAsync_RealRfc3986_ExtractsUriGrammar()
    {
        string fixturePath = Path.Combine("TestData", "rfc3986.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(3986, document.Metadata.Number);
        Assert.Contains("URI", document.Metadata.Title);
        Assert.True(document.Sections.Count > 5, $"Expected >5 sections, got {document.Sections.Count}");

        Assert.NotEmpty(document.AbnfBlocks);
        bool hasUriRule = document.AbnfBlocks.Any(b =>
            b.RuleNames.Contains("URI", StringComparer.Ordinal));
        Assert.True(hasUriRule, "Expected extracted ABNF to contain 'URI' rule");
    }

    [Fact]
    public async Task ParseAsync_RealRfc9000_ExtractsQuicTransport()
    {
        string fixturePath = Path.Combine("TestData", "rfc9000.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(9000, document.Metadata.Number);
        Assert.Contains("QUIC", document.Metadata.Title);
        Assert.True(document.Sections.Count > 3, $"Expected >3 sections, got {document.Sections.Count}");

        Assert.NotEmpty(document.AbnfBlocks);
    }

    [Fact]
    public async Task PageHeaders_FormFeed_StripsSubsequentPageHeaders()
    {
        string fixturePath = Path.Combine("TestData", "rfc9999.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(9999, document.Metadata.Number);

        string combinedText = string.Join('\n', document.Sections.Select(s => s.Text));
        Assert.DoesNotContain("[Page 2]", combinedText);
        Assert.DoesNotContain("RFC 9999    Test RFC for Page Headers    June 2026", combinedText);

        RfcSection? section2 = document.Sections.FirstOrDefault(s => s.Section == "2");
        Assert.NotNull(section2);
        Assert.Contains("page 2 content", section2!.Text);
    }

    [Fact]
    public async Task ParseAsync_RealRfc8446_DetectsTlsPresentationLang()
    {
        string fixturePath = Path.Combine("TestData", "rfc8446.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(GrammarStyleConstants.TlsPresentationLang, document.Metadata.GrammarStyle);
    }

    [Fact]
    public async Task ParseAsync_RealRfc9110_DetectsAbnf()
    {
        string fixturePath = Path.Combine("TestData", "rfc9110.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(GrammarStyleConstants.Abnf, document.Metadata.GrammarStyle);
    }

    [Fact]
    public async Task ParseAsync_RealRfc2119_DetectsNone()
    {
        string fixturePath = Path.Combine("TestData", "rfc2119.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(GrammarStyleConstants.None, document.Metadata.GrammarStyle);
    }

    [Fact]
    public async Task ParseAsync_RealRfc9052_DetectsCddl()
    {
        string fixturePath = Path.Combine("TestData", "rfc9052.txt");
        RfcDocument document = await parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(GrammarStyleConstants.Cddl, document.Metadata.GrammarStyle);
    }

    [Fact]
    public void XmlParser_ParseContent_ExtractsSectionsFromRfc2NamespacedXml()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rfc xmlns="urn:ietf:params:xml:ns:rfcxml"
                 number="9999"
                 category="std">
              <front>
                <title>Test RFC</title>
                <author fullname="Alice Author"/>
                <date year="2024"/>
              </front>
              <middle>
                <section>
                  <name>Introduction</name>
                  <t>This document describes something important.</t>
                  <t>It has two paragraphs.</t>
                  <section>
                    <name>Background</name>
                    <t>Background information goes here.</t>
                  </section>
                </section>
                <section>
                  <name>Protocol</name>
                  <t>The protocol works as follows.</t>
                </section>
              </middle>
            </rfc>
            """;

        RfcDocument document = xmlParser.ParseContent(xml, "rfc9999.xml");

        Assert.Equal(9999, document.Metadata.Number);
        Assert.Equal("Test RFC", document.Metadata.Title);
        Assert.Equal(3, document.Sections.Count);

        RfcSection section1 = document.Sections[0];
        Assert.Equal("1", section1.Section);
        Assert.Equal("Introduction", section1.Heading);
        Assert.Contains("something important", section1.Text);
        Assert.Contains("two paragraphs", section1.Text);

        RfcSection section11 = document.Sections[1];
        Assert.Equal("1.1", section11.Section);
        Assert.Equal("Background", section11.Heading);

        RfcSection section2 = document.Sections[2];
        Assert.Equal("2", section2.Section);
        Assert.Equal("Protocol", section2.Heading);
    }

    [Fact]
    public void XmlParser_ParseContent_HandlesBareRfcElement()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rfc number="1234">
              <front><title>A Test</title></front>
              <middle>
                <section>
                  <name>Only Section</name>
                  <t>Some text here.</t>
                </section>
              </middle>
            </rfc>
            """;

        RfcDocument document = xmlParser.ParseContent(xml, "rfc1234.xml");

        Assert.Equal(1234, document.Metadata.Number);
        Assert.Single(document.Sections);
        Assert.Equal("Only Section", document.Sections[0].Heading);
    }

    [Fact]
    public void XmlParser_ParseContent_ReturnsEmptyDocumentForMalformedXml()
    {
        const string xml = "not valid xml <<< >";

        RfcDocument document = xmlParser.ParseContent(xml, "rfc4242.xml");

        Assert.Equal(4242, document.Metadata.Number);
        Assert.Empty(document.Sections);
    }

    [Fact]
    public void XmlParser_ParseContent_ThrowsFormatExceptionForInvalidFilename()
    {
        Assert.Throws<FormatException>(() =>
            xmlParser.ParseContent("<rfc/>", "badfile.xml"));
    }

    [Fact]
    public void XmlParser_ParseContent_SetsSourcePathAndUrl()
    {
        const string xml = """
            <rfc>
              <front><title>Url Test</title></front>
              <middle>
                <section><name>Section</name><t>text</t></section>
              </middle>
            </rfc>
            """;

        RfcDocument document = xmlParser.ParseContent(xml, "rfc7777.xml");

        RfcSection section = document.Sections[0];
        Assert.Equal("rfc7777.xml", section.SourcePath);
        Assert.Contains("rfc-editor.org", section.Url);
        Assert.Contains("7777", section.Url);
    }
}
