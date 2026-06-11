namespace RfcRag.Cli;

internal sealed class CliCommandRouter(ISearchService searchService, ILoggerFactory loggerFactory)
{
    private readonly ILogger logger = loggerFactory.CreateLogger("RfcRag");

    /// <summary>
    /// Returns <c>true</c> if a CLI or benchmark command was detected and handled (caller should exit),
    /// or <c>false</c> if neither --cli nor --benchmark was present in <paramref name="args"/>.
    /// </summary>
    public async Task<bool> TryHandleAsync(string[] args, CancellationToken cancellationToken)
    {
        int cliArgIndex = Array.FindIndex(args,
            a => string.Equals(a, "--cli", StringComparison.OrdinalIgnoreCase));

        if (cliArgIndex >= 0)
        {
            string[] cliArgs = args[(cliArgIndex + 1)..];
            var command = new CliCommand(searchService, loggerFactory.CreateLogger<CliCommand>());
            await command.RunAsync(cliArgs, cancellationToken).ConfigureAwait(false);
            return true;
        }

        int benchmarkArgIndex = Array.FindIndex(args,
            a => string.Equals(a, "--benchmark", StringComparison.OrdinalIgnoreCase));

        if (benchmarkArgIndex >= 0)
        {
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

        return false;
    }
}
