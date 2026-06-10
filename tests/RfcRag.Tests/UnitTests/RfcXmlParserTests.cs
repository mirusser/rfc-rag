using RfcRag.Models;
using RfcRag.Parsing;

namespace RfcRag.Tests.UnitTests;

public sealed class RfcXmlParserTests
{
    private readonly RfcXmlParser parser = new();

    [Fact]
    public void ParseContent_Sections_HaveUniqueNonEmptyIds()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rfc xmlns="urn:ietf:params:xml:ns:rfcxml" number="9999">
              <front><title>Test RFC</title></front>
              <middle>
                <section>
                  <name>Introduction</name>
                  <t>Some introductory text.</t>
                  <section>
                    <name>Background</name>
                    <t>Background text.</t>
                  </section>
                </section>
                <section>
                  <name>Protocol</name>
                  <t>Protocol description.</t>
                </section>
              </middle>
            </rfc>
            """;

        RfcDocument doc = parser.ParseContent(xml, "rfc9999.xml");

        Assert.Equal(3, doc.Sections.Count);

        foreach (RfcSection section in doc.Sections)
            Assert.NotEqual(Guid.Empty, section.Id);

        var ids = doc.Sections.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ParseContent_MultipleSections_AllIdsUnique()
    {
        const string xml = """
            <rfc number="1234">
              <front><title>Multi-section Test</title></front>
              <middle>
                <section><name>One</name><t>text</t></section>
                <section><name>Two</name><t>text</t></section>
                <section><name>Three</name><t>text</t></section>
                <section><name>Four</name><t>text</t></section>
                <section><name>Five</name><t>text</t></section>
              </middle>
            </rfc>
            """;

        RfcDocument doc = parser.ParseContent(xml, "rfc1234.xml");

        Assert.Equal(5, doc.Sections.Count);
        var ids = doc.Sections.Select(s => s.Id).ToList();
        Assert.Equal(5, ids.Distinct().Count());
        Assert.All(ids, id => Assert.NotEqual(Guid.Empty, id));
    }
}
