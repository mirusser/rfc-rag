namespace RfcRag.Indexing;

/// <summary>
/// Resolves RFC source files from a mirror directory, enforcing one source per RFC number.
/// TXT is always preferred; XML is used only for numbers that have no TXT counterpart.
/// Same-extension duplicates across subdirectories are broken lexicographically (smallest path wins).
/// </summary>
internal static class RfcSourceResolver
{
    internal readonly record struct RfcSourceFile(string Path, int RfcNumber);

    internal static IReadOnlyList<RfcSourceFile> Resolve(string mirrorPath, RfcParserType parserType)
    {
        mirrorPath = ExpandPath(mirrorPath);

        var txtByNumber = new Dictionary<int, string>();
        foreach (string path in Directory.EnumerateFiles(mirrorPath, "rfc*.txt", SearchOption.AllDirectories))
        {
            if (!TryParseRfcNumber(path, out int number)) continue;
            if (!txtByNumber.TryGetValue(number, out string? existing) ||
                StringComparer.Ordinal.Compare(path, existing) < 0)
            {
                txtByNumber[number] = path;
            }
        }

        if (parserType == RfcParserType.Text)
        {
            return txtByNumber.Select(kv => new RfcSourceFile(kv.Value, kv.Key)).ToArray();
        }

        // Xml mode: include .xml only for numbers that have no .txt
        var result = new List<RfcSourceFile>(txtByNumber.Select(kv => new RfcSourceFile(kv.Value, kv.Key)));

        var xmlByNumber = new Dictionary<int, string>();
        foreach (string path in Directory.EnumerateFiles(mirrorPath, "rfc*.xml", SearchOption.AllDirectories))
        {
            if (!TryParseRfcNumber(path, out int number)) continue;
            if (txtByNumber.ContainsKey(number)) continue;
            if (!xmlByNumber.TryGetValue(number, out string? existing) ||
                StringComparer.Ordinal.Compare(path, existing) < 0)
            {
                xmlByNumber[number] = path;
            }
        }

        foreach (var kv in xmlByNumber)
            result.Add(new RfcSourceFile(kv.Value, kv.Key));

        return result;
    }

    internal static string ExpandPath(string mirrorPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mirrorPath);

        if (string.Equals(mirrorPath, "~", StringComparison.Ordinal))
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (mirrorPath.StartsWith("~/", StringComparison.Ordinal))
            return Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                mirrorPath[2..]);

        return mirrorPath;
    }

    internal static bool TryParseRfcNumber(string path, out int rfcNumber)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Length > 3 &&
            fileName.StartsWith("rfc", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(fileName[3..], out rfcNumber))
        {
            return true;
        }

        rfcNumber = 0;
        return false;
    }
}
