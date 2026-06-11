using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RfcRag.Indexing;

internal static class IndexingExtensions
{
    public static IServiceCollection AddRfcRagIndexing(this IServiceCollection services)
    {
        services.TryAddSingleton<IndexingRepository>();
        services.TryAddSingleton<IIndexerService, RfcIndexer>();

        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
            return opts.EmbeddingProvider == EmbeddingProvider.Local
                ? CreateLocalEmbeddingGenerator(opts)
                : CreateOpenRouterEmbeddingGenerator(opts);
        });

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<EmbeddingRetryPolicy>();

        services.TryAddSingleton<EmbeddingService>(sp =>
        {
            var generator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            var options = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<EmbeddingService>>();
            var retryPolicy = sp.GetRequiredService<EmbeddingRetryPolicy>();
            return new EmbeddingService(
                generator,
                retryPolicy,
                options.EmbeddingBatchSize,
                options.EmbeddingDimensions,
                options.MaxEmbeddingConcurrency,
                logger);
        });

        return services;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateOpenRouterEmbeddingGenerator(RfcRagOptions opts)
    {
        string? openRouterKey = Environment.GetEnvironmentVariable(RfcRagOptions.OpenRouterApiKeyEnvironmentVariable);
        return string.IsNullOrWhiteSpace(openRouterKey)
            ? new MissingApiKeyEmbeddingGenerator()
            : OpenAiEmbeddingGeneratorAdapter.Create(openRouterKey, opts.OpenRouterEmbeddingEndpoint, opts.EmbeddingModel);
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateLocalEmbeddingGenerator(RfcRagOptions opts) =>
        OpenAiEmbeddingGeneratorAdapter.Create("local", opts.LocalEmbeddingEndpoint, opts.EmbeddingModel);
}
