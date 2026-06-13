using System.Text.Json;

namespace RfcRag.Cli;

internal sealed class CliCommand(ISearchService searchService, ContextAssembler contextAssembler, ILogger<CliCommand> logger, IAskService? askService = null)
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Returns 0 on success, non-zero on error.</summary>
    public Task<int> RunAsync(string[] cliArgs, CancellationToken cancellationToken) =>
        RunAsync(cliArgs, Console.Out, cancellationToken);

    /// <summary>Runs a CLI command with the given arguments, writing output to the specified writer.</summary>
    /// <param name="cliArgs">Command-line arguments (verb + arguments).</param>
    /// <param name="output">Text writer for command output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>0 on success, non-zero on error.</returns>
    public async Task<int> RunAsync(string[] cliArgs, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cliArgs);
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
            "EVIDENCE" => await RunEvidenceAsync(cliArgs, output, cancellationToken).ConfigureAwait(false),
            "ASK" => await RunAskAsync(cliArgs, output, cancellationToken).ConfigureAwait(false),
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
        int limit = CliArgParser.ParseIntFlag(args, "--limit", 10);

        var results = await searchService.SearchAsync(query, limit, normativeKeyword: null, includeObsolete: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await output.WriteLineAsync(JsonSerializer.Serialize(results, jsonOptions)).ConfigureAwait(false);
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

        await output.WriteLineAsync(JsonSerializer.Serialize(section, jsonOptions)).ConfigureAwait(false);
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
        int rfcFlag = CliArgParser.ParseIntFlag(args, "--rfc", -1);
        int[]? rfcFilter = rfcFlag > 0 ? [rfcFlag] : null;

        var results = await searchService.SearchNormativeAsync(
            keyword, rfcFilter, limit: 20, cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync(JsonSerializer.Serialize(results, jsonOptions)).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RunStatsAsync(TextWriter output, CancellationToken cancellationToken)
    {
        string stats = await searchService.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(stats).ConfigureAwait(false);
        return 0;
    }

    private int PrintUnknownVerb(string verb)
    {
        logger.LogError("Unknown verb: '{Verb}'", verb);
        PrintUsage();
        return 1;
    }

    private void PrintUsage()
    {
        logger.LogInformation("Usage: --cli <verb> [args]");
        logger.LogInformation("  search <query> [--limit N]");
        logger.LogInformation("  section <rfcNumber> <sectionId>");
        logger.LogInformation("  normative <keyword> [--rfc N]");
        logger.LogInformation("  evidence <query> [--limit N] [--budget N]");
        logger.LogInformation("  ask <question> [--limit N] [--keyword KW]");
        logger.LogInformation("  stats");
    }

    private async Task<int> RunEvidenceAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            logger.LogError("Usage: --cli evidence <query> [--limit N] [--budget N]");
            return 1;
        }

        string query = args[1];
        int limit = CliArgParser.ParseIntFlag(args, "--limit", 10);
        int budget = CliArgParser.ParseIntFlag(args, "--budget", 10000);

        var results = await searchService.SearchAsync(
            query, limit, normativeKeyword: null, includeObsolete: false, cancellationToken: cancellationToken).ConfigureAwait(false);

        var pack = await contextAssembler.AssembleAsync(
            query, results, budget, includeObsolete: false, cancellationToken: cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync(JsonSerializer.Serialize(pack, jsonOptions)).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RunAskAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            logger.LogError("Usage: --cli ask <question> [--limit N] [--keyword KW]");
            return 1;
        }

        if (askService is null)
        {
            logger.LogError("Ask verb requires a chat model to be configured (RfcRag__ChatModel).");
            return 1;
        }

        string question = args[1];
        int limit = CliArgParser.ParseIntFlag(args, "--limit", 0);
        string? keyword = CliArgParser.ParseStringFlag(args, "--keyword");
        int? effectiveLimit = limit > 0 ? limit : null;

        var answer = await askService.AskAsync(question, effectiveLimit, keyword, includeObsolete: false, cancellationToken: cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync(JsonSerializer.Serialize(answer, jsonOptions)).ConfigureAwait(false);
        return 0;
    }

}
