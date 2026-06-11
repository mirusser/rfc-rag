using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI;

namespace RfcRag.Answering;

internal static class AnsweringExtensions
{
    public static IServiceCollection AddRfcRagAnswering(this IServiceCollection services)
    {
        services.TryAddSingleton<IChatClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
            return opts.ChatProvider == ChatProvider.Local
                ? CreateLocalChatClient(opts)
                : CreateOpenRouterChatClient(opts);
        });

        services.TryAddSingleton<ContextAssembler>();
        services.TryAddSingleton<AnswerGenerator>();
        services.TryAddSingleton<IAskService, AskService>();

        return services;
    }

    private static IChatClient CreateOpenRouterChatClient(RfcRagOptions opts)
    {
        string? apiKey = Environment.GetEnvironmentVariable(RfcRagOptions.OpenRouterApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"OpenRouter API key is required for chat. Set the {RfcRagOptions.OpenRouterApiKeyEnvironmentVariable} environment variable.");
        }

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(opts.OpenRouterEmbeddingEndpoint),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };
        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), clientOptions);
        return openAiClient.GetChatClient(opts.ChatModel ?? throw new InvalidOperationException("ChatModel must be configured when using OpenRouter chat provider.")).AsIChatClient();
    }

    private static IChatClient CreateLocalChatClient(RfcRagOptions opts)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(opts.LocalEmbeddingEndpoint),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };
        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential("local"), clientOptions);
        return openAiClient.GetChatClient(opts.ChatModel ?? throw new InvalidOperationException("ChatModel must be configured when using local chat provider.")).AsIChatClient();
    }
}
