using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RfcRag.Parsing;

/// <summary>
/// Parses IETF RFC XML 2 format files (RFC 7991) into <see cref="RfcDocument"/>.
/// Falls back to returning an empty document for malformed XML rather than throwing.
/// </summary>
internal sealed partial class RfcXmlParser
{
    public async Task<RfcDocument> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string rawXml = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return ParseContent(rawXml, Path.GetFileName(filePath));
    }

    public RfcDocument ParseContent(string rawXml, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawXml);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        int rfcNumber = ExtractRfcNumber(fileName);
        if (rfcNumber == 0)
        {
            throw new FormatException($"Could not extract RFC number from filename '{fileName}'.");
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(rawXml);
        }
        catch (System.Xml.XmlException)
        {
            return EmptyDocument(rfcNumber, fileName);
        }

        XElement? root = doc.Root;
        if (root is null)
            return EmptyDocument(rfcNumber, fileName);

        // Support both namespaced (<rfc xmlns="urn:ietf:params:xml:ns:rfcxml">) and bare <rfc> elements
        XNamespace ns = root.Name.Namespace;
        string title = root.Descendants(ns + "title").FirstOrDefault()?.Value.Trim()
            ?? $"RFC {rfcNumber}";

        string url = $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}";

        var metadata = new RfcMetadata
        {
            Number = rfcNumber,
            Title = title,
            Date = root.Descendants(ns + "date").FirstOrDefault()?.Attribute("year")?.Value ?? string.Empty,
            Category = root.Attribute("category")?.Value ?? string.Empty,
            Authors = ExtractAuthors(root, ns)
        };

        var sections = ExtractSections(root, ns, rfcNumber, title, fileName, url);

        return new RfcDocument
        {
            Metadata = metadata,
            Sections = sections,
            AbnfBlocks = [],
            NormativeOccurrences = []
        };
    }

    private static IReadOnlyList<RfcSection> ExtractSections(
        XElement root,
        XNamespace ns,
        int rfcNumber,
        string title,
        string fileName,
        string url)
    {
        var sections = new List<RfcSection>();

        // RFC XML 2: sections live under <middle> and optionally <back>
        var middleElement = root.Descendants(ns + "middle").FirstOrDefault() ?? root;
        var topLevelSections = middleElement.Elements(ns + "section").ToList();

        for (int i = 0; i < topLevelSections.Count; i++)
        {
            CollectSections(topLevelSections[i], ns, rfcNumber, title, fileName, url,
                (i + 1).ToString(), sections);
        }

        return sections;
    }

    private static void CollectSections(
        XElement sectionElement,
        XNamespace ns,
        int rfcNumber,
        string title,
        string fileName,
        string url,
        string sectionNumber,
        List<RfcSection> result)
    {
        string? heading = sectionElement.Element(ns + "name")?.Value.Trim();
        string text = ExtractText(sectionElement, ns);

        if (!string.IsNullOrWhiteSpace(text))
        {
            result.Add(new RfcSection
            {
                Id = Guid.NewGuid(),
                RfcNumber = rfcNumber,
                Title = title,
                Section = sectionNumber,
                Heading = heading,
                Text = text,
                SourcePath = fileName,
                Url = url
            });
        }

        var children = sectionElement.Elements(ns + "section").ToList();
        for (int i = 0; i < children.Count; i++)
        {
            CollectSections(children[i], ns, rfcNumber, title, fileName, url,
                $"{sectionNumber}.{i + 1}", result);
        }
    }

    private static string ExtractText(XElement section, XNamespace ns)
    {
        var textParts = section
            .Elements(ns + "t")
            .Select(t => t.Value.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t));

        return string.Join("\n\n", textParts);
    }

    private static string[] ExtractAuthors(XElement root, XNamespace ns)
    {
        return root.Descendants(ns + "author")
            .Select(a => a.Attribute("fullname")?.Value ?? a.Attribute("surname")?.Value ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
    }

    private static RfcDocument EmptyDocument(int rfcNumber, string fileName)
    {
        return new RfcDocument
        {
            Metadata = new RfcMetadata { Number = rfcNumber, Title = $"RFC {rfcNumber}" },
            Sections = [],
            AbnfBlocks = [],
            NormativeOccurrences = []
        };
    }

    private static int ExtractRfcNumber(string fileName)
    {
        var match = RfcNumberRegex().Match(fileName);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(1000);
    private static readonly Regex RfcNumberRegexInstance =
        new(@"rfc(\d+)\.(xml|txt)$", RegexOptions.IgnoreCase, RegexTimeout);

    private static Regex RfcNumberRegex() => RfcNumberRegexInstance;
}
