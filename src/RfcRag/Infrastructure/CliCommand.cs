using System.Text.Json;

namespace RfcRag.Infrastructure;

internal sealed class CliCommand(ISearchService searchService, ILogger<CliCommand> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Returns 0 on success, non-zero on error.</summary>
    public Task<int> RunAsync(string[] cliArgs, CancellationToken cancellationToken) =>
        RunAsync(cliArgs, Console.Out, cancellationToken);

    public async Task<int> RunAsync(string[] cliArgs, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (cliArgs.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        return cliArgs[0].ToUpperInvariant() switch
        {
            "SEARCH" => await RunSearchAsync(cliArgs, output, cancellationToken).ConfigureAwait(false),
            "SECTION" => await RunSectionAsync(cliArgs, output, cancellationToken).ConfigureAwait(false),
            "NORMATIVE" => await RunNormativeAsync(cliArgs, output, cancellationToken).ConfigureAwait(false),
            "STATS" => await RunStatsAsync(output, cancellationToken).ConfigureAwait(false),
            _ => PrintUnknownVerb(cliArgs[0])
        };
    }

    private async Task<int> RunSearchAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            logger.LogError("Usage: --cli search <query> [--limit N]");
            return 1;
        }

        string query = args[1];
        int limit = ParseIntFlag(args, "--limit", defaultValue: 10);

        var results = await searchService.SearchAsync(query, limit, normativeKeyword: null, cancellationToken)
            .ConfigureAwait(false);

        await output.WriteLineAsync(JsonSerializer.Serialize(results, JsonOptions)).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RunSectionAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        if (args.Length < 3 || !int.TryParse(args[1], out int rfcNumber))
        {
            logger.LogError("Usage: --cli section <rfcNumber> <sectionId>");
            return 1;
        }

        string sectionId = args[2];
        var section = await searchService.GetSectionAsync(rfcNumber, sectionId, cancellationToken)
            .ConfigureAwait(false);

        await output.WriteLineAsync(JsonSerializer.Serialize(section, JsonOptions)).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RunNormativeAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            logger.LogError("Usage: --cli normative <keyword> [--rfc N]");
            return 1;
        }

        string keyword = args[1];
        int? rfcNumber = null;
        int rfcFlag = ParseIntFlag(args, "--rfc", defaultValue: -1);
        if (rfcFlag > 0)
            rfcNumber = rfcFlag;

        int[]? rfcFilter = rfcNumber is { } n ? [n] : null;
        var results = await searchService.SearchNormativeAsync(
            keyword,
            rfcFilter,
            limit: 20,
            cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync(JsonSerializer.Serialize(results, JsonOptions)).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RunStatsAsync(TextWriter output, CancellationToken cancellationToken)
    {
        string stats = await searchService.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(stats).ConfigureAwait(false);
        return 0;
    }

    private static int PrintUnknownVerb(string verb)
    {
        Console.Error.WriteLine($"Unknown verb: '{verb}'");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: --cli <verb> [args]");
        Console.Error.WriteLine("  search <query> [--limit N]");
        Console.Error.WriteLine("  section <rfcNumber> <sectionId>");
        Console.Error.WriteLine("  normative <keyword> [--rfc N]");
        Console.Error.WriteLine("  stats");
    }

    private static int ParseIntFlag(string[] args, string flag, int defaultValue)
    {
        int idx = Array.IndexOf(args, flag);
        if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int value))
            return value;
        return defaultValue;
    }
}
