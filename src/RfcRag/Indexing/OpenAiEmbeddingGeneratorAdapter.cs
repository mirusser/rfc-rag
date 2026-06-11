using System.ClientModel.Primitives;
using OpenAI;

namespace RfcRag.Indexing;

/// <summary>
/// Factory for creating an <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> backed by
/// OpenRouter (or any OpenAI-compatible endpoint). Uses the official MEAI adapter from
/// <c>Microsoft.Extensions.AI.OpenAI</c> which handles encoding format negotiation correctly.
/// Retries are disabled at the HTTP level (maxRetries: 0) and delegated to <see cref="EmbeddingRetryPolicy"/>.
/// </summary>
internal static class OpenAiEmbeddingGeneratorAdapter
{
    internal static IEmbeddingGenerator<string, Embedding<float>> Create(
        string apiKey, string endpoint, string model)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };
        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), clientOptions);
        return openAiClient.GetEmbeddingClient(model).AsIEmbeddingGenerator();
    }
}
