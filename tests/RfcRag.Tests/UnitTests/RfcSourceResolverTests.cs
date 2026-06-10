using RfcRag.Indexing;
using RfcRag.Settings;

namespace RfcRag.Tests.UnitTests;

public sealed class RfcSourceResolverTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public RfcSourceResolverTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public void Resolve_TxtAndXmlSameNumber_PrefersTxt()
    {
        CreateFile("rfc9000.txt");
        CreateFile("rfc9000.xml");

        var sources = RfcSourceResolver.Resolve(tempDir, RfcParserType.Xml);

        var source = Assert.Single(sources, s => s.RfcNumber == 9000);
        Assert.EndsWith(".txt", source.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_XmlOnlyNumber_IncludedInXmlMode()
    {
        CreateFile("rfc9001.xml");

        var sources = RfcSourceResolver.Resolve(tempDir, RfcParserType.Xml);

        var source = Assert.Single(sources, s => s.RfcNumber == 9001);
        Assert.EndsWith(".xml", source.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_TextMode_IgnoresXml()
    {
        CreateFile("rfc9001.txt");
        CreateFile("rfc9002.xml");

        var sources = RfcSourceResolver.Resolve(tempDir, RfcParserType.Text);

        Assert.Contains(sources, s => s.RfcNumber == 9001);
        Assert.DoesNotContain(sources, s => s.RfcNumber == 9002);
    }

    [Fact]
    public void Resolve_DuplicateTxtAcrossSubdirs_PicksDeterministically()
    {
        string subA = Path.Combine(tempDir, "a");
        string subB = Path.Combine(tempDir, "b");
        Directory.CreateDirectory(subA);
        Directory.CreateDirectory(subB);
        File.WriteAllText(Path.Combine(subA, "rfc9000.txt"), "content");
        File.WriteAllText(Path.Combine(subB, "rfc9000.txt"), "content");

        var sources = RfcSourceResolver.Resolve(tempDir, RfcParserType.Text);
        var source = Assert.Single(sources, s => s.RfcNumber == 9000);

        // Lexicographically smallest path wins
        string pathA = Path.Combine(subA, "rfc9000.txt");
        string pathB = Path.Combine(subB, "rfc9000.txt");
        string expected = StringComparer.Ordinal.Compare(pathA, pathB) < 0 ? pathA : pathB;
        Assert.Equal(expected, source.Path);
    }

    [Fact]
    public void Resolve_TildePath_ExpandsToUserProfile()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string expanded = RfcSourceResolver.ExpandPath("~/some/path");

        Assert.StartsWith(userProfile, expanded, StringComparison.Ordinal);
        Assert.EndsWith("some/path", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParseRfcNumber_ValidName_ReturnsTrue()
    {
        Assert.True(RfcSourceResolver.TryParseRfcNumber("path/rfc9000.txt", out int number));
        Assert.Equal(9000, number);
    }

    [Fact]
    public void TryParseRfcNumber_InvalidName_ReturnsFalse()
    {
        Assert.False(RfcSourceResolver.TryParseRfcNumber("path/badfile.txt", out _));
    }

    private void CreateFile(string name) =>
        File.WriteAllText(Path.Combine(tempDir, name), "content");
}
