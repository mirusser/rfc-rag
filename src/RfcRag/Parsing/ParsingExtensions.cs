using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RfcRag.Parsing;

internal static class ParsingExtensions
{
    public static IServiceCollection AddRfcRagParsing(this IServiceCollection services)
    {
        services.TryAddSingleton<RfcParser>();
        services.TryAddSingleton<RfcXmlParser>();
        return services;
    }
}
