namespace RfcRag.Settings;

internal sealed class RfcRagOptionsValidator : IValidateOptions<RfcRagOptions>
{
    public ValidateOptionsResult Validate(string? name, RfcRagOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RfcMirrorPath))
            failures.Add($"{nameof(options.RfcMirrorPath)} must not be empty.");

        if (string.IsNullOrWhiteSpace(options.PostgresConnectionString))
            failures.Add($"{nameof(options.PostgresConnectionString)} must not be empty.");

        if (string.IsNullOrWhiteSpace(options.EmbeddingModel))
            failures.Add($"{nameof(options.EmbeddingModel)} must not be empty.");

        if (options.EmbeddingBatchSize is < 1 or > 2048)
            failures.Add($"{nameof(options.EmbeddingBatchSize)} must be between 1 and 2048 (got {options.EmbeddingBatchSize}).");

        if (options.EmbeddingDimensions is < 1 or > 16000)
            failures.Add($"{nameof(options.EmbeddingDimensions)} must be between 1 and 16000 (got {options.EmbeddingDimensions}).");

        if (options.MaxIndexingParallelism < 1)
            failures.Add($"{nameof(options.MaxIndexingParallelism)} must be at least 1 (got {options.MaxIndexingParallelism}).");

        if (options.MaxEmbeddingConcurrency < 1)
            failures.Add($"{nameof(options.MaxEmbeddingConcurrency)} must be at least 1 (got {options.MaxEmbeddingConcurrency}).");

        if (!IsAbsoluteHttpUri(options.OpenRouterEmbeddingEndpoint))
            failures.Add($"{nameof(options.OpenRouterEmbeddingEndpoint)} must be an absolute http(s) URI (got '{options.OpenRouterEmbeddingEndpoint}').");

        if (!IsAbsoluteHttpUri(options.LocalEmbeddingEndpoint))
            failures.Add($"{nameof(options.LocalEmbeddingEndpoint)} must be an absolute http(s) URI (got '{options.LocalEmbeddingEndpoint}').");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAbsoluteHttpUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        return Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)
            && (string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
    }
}
