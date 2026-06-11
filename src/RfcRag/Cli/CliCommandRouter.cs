namespace RfcRag.Cli;

internal sealed class CliCommandRouter(
    ISearchService searchService,
    IndexingRepository indexingRepository,
    ILoggerFactory loggerFactory,
    IAskService? askService = null)
{
    private readonly ILogger logger = loggerFactory.CreateLogger("RfcRag");

    /// <summary>
    /// Returns <c>true</c> if a CLI, benchmark, or eval command was detected and handled (caller should exit),
    /// or <c>false</c> if none of those flags were present in <paramref name="args"/>.
    /// </summary>
    public async Task<bool> TryHandleAsync(string[] args, CancellationToken cancellationToken)
    {
        int cliArgIndex = Array.FindIndex(args,
            a => string.Equals(a, "--cli", StringComparison.OrdinalIgnoreCase));

        if (cliArgIndex >= 0)
        {
            string[] cliArgs = args[(cliArgIndex + 1)..];
            var command = new CliCommand(searchService, new ContextAssembler(searchService),
                loggerFactory.CreateLogger<CliCommand>(), askService);
            await command.RunAsync(cliArgs, cancellationToken).ConfigureAwait(false);
            return true;
        }

        int benchmarkArgIndex = Array.FindIndex(args,
            a => string.Equals(a, "--benchmark", StringComparison.OrdinalIgnoreCase));

        if (benchmarkArgIndex >= 0)
        {
            logger.LogWarning(
                "--benchmark is deprecated and will be removed in a future version. Use --eval instead.");

            string? queriesFilePath = benchmarkArgIndex + 1 < args.Length
                ? args[benchmarkArgIndex + 1]
                : null;

            if (string.IsNullOrWhiteSpace(queriesFilePath) || queriesFilePath.StartsWith("--", StringComparison.Ordinal))
            {
                logger.LogError("Usage: --benchmark <queries-file-path>");
                return true;
            }

            var command = new BenchmarkCommand(searchService, loggerFactory.CreateLogger<BenchmarkCommand>());
            await command.RunAsync(queriesFilePath, topK: 10, cancellationToken).ConfigureAwait(false);
            return true;
        }

        bool answerMode = args.Any(a =>
            string.Equals(a, "--answers", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, "--answer", StringComparison.OrdinalIgnoreCase));

        int evalArgIndex = Array.FindIndex(args,
            a => string.Equals(a, "--eval", StringComparison.OrdinalIgnoreCase));

        if (evalArgIndex >= 0)
        {
            string? questionsFilePath = evalArgIndex + 1 < args.Length
                && !args[evalArgIndex + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[evalArgIndex + 1]
                : null;

            if (string.IsNullOrWhiteSpace(questionsFilePath))
            {
                logger.LogError("Usage: --eval <golden-questions-file-path> [--answers] [--corpus testdata|full|all] [--limit N]");
                return true;
            }

            string corpus = ParseStringArg(args, "--corpus", "testdata");
            int topK = ParseIntArg(args, "--limit", 10);

            var command = new EvalCommand(searchService, indexingRepository,
                TimeProvider.System, loggerFactory.CreateLogger<EvalCommand>(), askService);

            if (answerMode)
            {
                await command.RunAnswerEvalAsync(questionsFilePath, topK, corpus, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await command.RunRetrievalEvalAsync(questionsFilePath, topK, corpus, cancellationToken)
                    .ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }

    private static string ParseStringArg(string[] args, string flag, string defaultValue)
    {
        int idx = Array.IndexOf(args, flag);
        if (idx >= 0 && idx + 1 < args.Length)
            return args[idx + 1];
        return defaultValue;
    }

    private static int ParseIntArg(string[] args, string flag, int defaultValue)
    {
        int idx = Array.IndexOf(args, flag);
        if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int value))
            return value;
        return defaultValue;
    }
}
