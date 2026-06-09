using RfcRag.Indexing;
using RfcRag.Parsing;
using RfcRag.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI;

namespace RfcRag.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRfcRagServices(
        this IServiceCollection services,
        NpgsqlDataSource? dataSource = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (dataSource is not null)
        {
            services.TryAddSingleton(dataSource);
        }
        else
        {
            services.TryAddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
                ArgumentException.ThrowIfNullOrWhiteSpace(options.PostgresConnectionString);

                var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.PostgresConnectionString);
                dataSourceBuilder.UseVector();
                return dataSourceBuilder.Build();
            });
        }

        services.TryAddSingleton<RfcParser>();
        services.TryAddSingleton<RfcXmlParser>();
        services.TryAddSingleton<SearchRepository>();
        services.TryAddSingleton<MetadataRepository>();
        services.TryAddSingleton<IndexingRepository>();
        services.TryAddSingleton<IIndexerService, RfcIndexer>();
        services.TryAddSingleton<ISearchService, SearchService>();

        return services.AddRfcRagEmbeddings();
    }

    public static IServiceCollection AddRfcRagEmbeddings(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;

            if (opts.EmbeddingProvider == EmbeddingProvider.Local)
            {
                return CreateLocalEmbeddingGenerator(opts);
            }

            return CreateOpenRouterEmbeddingGenerator(opts);
        });

        services.TryAddSingleton<EmbeddingService>(sp =>
        {
            var generator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            var options = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<EmbeddingService>>();
            return new EmbeddingService(generator, options.EmbeddingBatchSize, options.MaxEmbeddingConcurrency, logger);
        });

        return services;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateOpenRouterEmbeddingGenerator(RfcRagOptions opts)
    {
        string? openRouterKey = Environment.GetEnvironmentVariable(RfcRagOptions.OpenRouterApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(openRouterKey))
        {
            return new MissingApiKeyEmbeddingGenerator();
        }

        var openAiOptions = new OpenAIClientOptions { Endpoint = new Uri(opts.OpenRouterEmbeddingEndpoint) };
        var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(openRouterKey), openAiOptions);
        return client.GetEmbeddingClient(opts.EmbeddingModel).AsIEmbeddingGenerator();
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateLocalEmbeddingGenerator(RfcRagOptions opts)
    {
        var openAiOptions = new OpenAIClientOptions { Endpoint = new Uri(opts.LocalEmbeddingEndpoint) };
        var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential("local"), openAiOptions);
        return client.GetEmbeddingClient(opts.EmbeddingModel).AsIEmbeddingGenerator();
    }
}
