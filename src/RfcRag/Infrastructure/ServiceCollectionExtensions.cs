using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RfcRag.Infrastructure;

internal static class ServiceCollectionExtensions
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

        return services
            .AddRfcRagParsing()
            .AddRfcRagSearch()
            .AddRfcRagIndexing();
    }
}
