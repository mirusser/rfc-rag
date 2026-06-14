using System.Globalization;
using System.Text.Json;

namespace RfcRag.Indexing;

internal static class ErrataLoader
{
    public static async Task<IReadOnlyList<RfcErratum>> LoadAsync(
        string path,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        string expandedPath = RfcSourceResolver.ExpandPath(path);
        if (!File.Exists(expandedPath))
        {
            logger.LogWarning("Errata snapshot '{ErrataPath}' does not exist; skipping errata ingestion.", expandedPath);
            return [];
        }

        try
        {
            FileStream stream = File.OpenRead(expandedPath);
            await using (stream.ConfigureAwait(false))
            {
                using JsonDocument document = await JsonDocument
                    .ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return ParseEntries(document.RootElement, logger);
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Errata snapshot '{ErrataPath}' is not valid JSON; skipping errata ingestion.", expandedPath);
            return [];
        }
    }

    private static IReadOnlyList<RfcErratum> ParseEntries(JsonElement root, ILogger logger)
    {
        JsonElement entries = root;
        if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, out JsonElement nested, "errata", "entries"))
        {
            entries = nested;
        }

        if (entries.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("Errata snapshot root is not an array; skipping errata ingestion.");
            return [];
        }

        var errata = new List<RfcErratum>();
        int index = 0;
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (TryParseEntry(entry, out RfcErratum? erratum, out string reason))
            {
                errata.Add(erratum);
            }
            else
            {
                logger.LogWarning("Skipping malformed errata entry at index {ErrataIndex}: {Reason}", index, reason);
            }

            index++;
        }

        return errata;
    }

    private static bool TryParseEntry(JsonElement entry, out RfcErratum erratum, out string reason)
    {
        erratum = new RfcErratum();
        reason = string.Empty;

        if (entry.ValueKind != JsonValueKind.Object)
        {
            reason = "entry is not a JSON object";
            return false;
        }

        if (!TryGetInt(entry, out int errataId, "errata_id", "eid", "id") || errataId <= 0)
        {
            reason = "missing errata id";
            return false;
        }

        if (!TryGetRfcNumber(entry, out int rfcNumber))
        {
            reason = "missing RFC number";
            return false;
        }

        string? status = RfcErratum.NormalizeStatus(GetString(entry, "errata_status_code", "status"));
        if (string.IsNullOrWhiteSpace(status))
        {
            reason = "missing errata status";
            return false;
        }

        erratum = new RfcErratum
        {
            ErrataId = errataId,
            RfcNumber = rfcNumber,
            Section = EmptyToNull(GetString(entry, "section")),
            Status = status,
            OriginalText = EmptyToNull(GetString(entry, "orig_text", "original_text")),
            CorrectedText = EmptyToNull(GetString(entry, "correct_text", "corrected_text")),
            ReportedDate = TryGetDate(entry, out DateOnly reportedDate) ? reportedDate : null,
        };

        return true;
    }

    private static bool TryGetRfcNumber(JsonElement entry, out int rfcNumber)
    {
        if (TryGetInt(entry, out rfcNumber, "rfc_number", "rfc"))
        {
            return rfcNumber > 0;
        }

        string? documentId = GetString(entry, "doc-id", "doc_id", "document");
        if (documentId is null || !documentId.StartsWith("RFC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(documentId[3..], NumberStyles.None, CultureInfo.InvariantCulture, out rfcNumber)
            && rfcNumber > 0;
    }

    private static bool TryGetInt(JsonElement entry, out int value, params string[] names)
    {
        string? raw = GetString(entry, names);
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetDate(JsonElement entry, out DateOnly value)
    {
        string? raw = GetString(entry, "submit_date", "reported_date", "reported_at");
        return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static string? GetString(JsonElement entry, params string[] names)
    {
        return TryGetProperty(entry, out JsonElement value, names) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;
    }

    private static bool TryGetProperty(JsonElement entry, out JsonElement value, params string[] names)
    {
        foreach (JsonProperty property in entry
                     .EnumerateObject()
                     .Where(property => names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)))
        {
            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
