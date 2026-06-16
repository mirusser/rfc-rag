using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel.Connectors.PgVector;

namespace RfcRag.Search;

internal static class SearchExtensions
{
    public static IServiceCollection AddRfcRagSearch(this IServiceCollection services)
    {
        services.TryAddSingleton<SearchRepository>();
        services.TryAddSingleton<MetadataRepository>();
        services.TryAddSingleton<ISearchService, SearchService>();

        // Register the VectorStore, reusing the NpgsqlDataSource (with UseVector()) already in DI.
        // No EnsureCollectionExistsAsync called — schema is owned by the migration runner.
        services.AddPostgresVectorStore();
        services.TryAddSingleton(sp =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            return new PostgresCollection<Guid, RfcSectionRecord>(
                dataSource,
                "rfc_sections",
                ownsDataSource: false,
                new PostgresCollectionOptions { Schema = "rfc_rag" });
        });

        services.TryAddSingleton<IVectorDataSearch, VectorDataSearch>();

        return services;
    }
}
