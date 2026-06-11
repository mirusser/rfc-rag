using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RfcRag.Search;

internal static class SearchExtensions
{
    public static IServiceCollection AddRfcRagSearch(this IServiceCollection services)
    {
        services.TryAddSingleton<SearchRepository>();
        services.TryAddSingleton<MetadataRepository>();
        services.TryAddSingleton<ISearchService, SearchService>();
        return services;
    }
}
