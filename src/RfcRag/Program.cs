using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using OpenTelemetry.Metrics;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
AddRfcRagConfiguration(builder.Configuration, args);

builder.Services
    .AddOptions<RfcRagOptions>()
    .Bind(builder.Configuration.GetSection(RfcRagOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<RfcRagOptions>, RfcRagOptionsValidator>();

builder.Services.AddRfcRagServices();

string? chatModel = builder.Configuration.GetSection(RfcRagOptions.SectionName)["ChatModel"];
string? chatProvider = builder.Configuration.GetSection(RfcRagOptions.SectionName)["ChatProvider"];
bool answeringEnabled = !string.IsNullOrWhiteSpace(chatModel) &&
    Enum.TryParse<ChatProvider>(chatProvider, ignoreCase: true, out var parsed) &&
    parsed.IsEnabled();
if (answeringEnabled)
{
    builder.Services.AddRfcRagAnswering();
}

// Track which tool types to register based on answering module state
var mcpToolTypes = new List<Type> { typeof(RfcRag.Tools.RfcRagTools) };
if (answeringEnabled)
{
    mcpToolTypes.Add(typeof(RfcRag.Tools.RfcAskTools));
}

builder.Services.AddSingleton<CliCommandRouter>();
builder.Services.AddSingleton<RfcRagStartupService>();

const string OtlpEndpointEnvVar = "OTEL_EXPORTER_OTLP_ENDPOINT";
string? otlpEndpoint = builder.Configuration[OtlpEndpointEnvVar];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics => metrics
            .AddMeter(EmbeddingService.EmbeddingsMeterName)
            .AddOtlpExporter());
}

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools(mcpToolTypes.AsEnumerable())
    .WithRequestFilters(filters =>
    {
        filters.AddCallToolFilter(next => (request, cancellationToken) =>
        {
            var services = request.Services;
            if (services is null)
            {
                return next(request, cancellationToken);
            }

            var logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RfcRag.ToolExceptionFilter");
            return ToolExceptionFilter.CreateSafetyNet(next, logger)(request, cancellationToken);
        });
    })
    .WithListResourcesHandler(static (_, _) =>
        new ValueTask<ListResourcesResult>(new ListResourcesResult { Resources = [] }))
    .WithListPromptsHandler(static (_, _) =>
        new ValueTask<ListPromptsResult>(new ListPromptsResult { Prompts = [] }));

var app = builder.Build();

var startupService = app.Services.GetRequiredService<RfcRagStartupService>();
if (await startupService.RunStartupAsync(args, CancellationToken.None).ConfigureAwait(false))
{
    await app.RunAsync().ConfigureAwait(false);
}

static void AddRfcRagConfiguration(IConfigurationBuilder configuration, string[] args)
{
    configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
    configuration.AddEnvironmentVariables();
    configuration.AddCommandLine(args);
}
