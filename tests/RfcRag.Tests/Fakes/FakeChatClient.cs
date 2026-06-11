using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace RfcRag.Tests.Fakes;

internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<string> scriptedResponses;
    private readonly List<ChatRequest> capturedCalls = [];

    public FakeChatClient(params string[] scriptedResponses)
    {
        this.scriptedResponses = new Queue<string>(scriptedResponses);
    }

    /// <summary>Captured chat requests in order of invocation.</summary>
    public IReadOnlyList<ChatRequest> CapturedCalls => capturedCalls.AsReadOnly();

    /// <summary>Model ID returned in responses. Defaults to "fake-model".</summary>
    public string ModelId { get; set; } = "fake-model";

    public static ChatClientMetadata Metadata => new("fake", new Uri("http://localhost"));

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CaptureCall(chatMessages, options);
        string response = GetNextResponse();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response))
        {
            ModelId = ModelId,
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CaptureCall(chatMessages, options);
        string response = GetNextResponse();
        yield return new ChatResponseUpdate(ChatRole.Assistant, response);
    }

    public object? GetService(Type? serviceType, object? key = null) => null;

    public void Dispose()
    {
    }

    private void CaptureCall(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        capturedCalls.Add(new ChatRequest
        {
            Messages = messages.Select(m => new CapturedMessage
            {
                Role = m.Role.ToString() ?? "",
                Text = m.Contents?.OfType<TextContent>().FirstOrDefault()?.Text ?? ""
            }).ToList(),
            Options = options
        });
    }

    private string GetNextResponse()
    {
        if (scriptedResponses.TryDequeue(out string? response))
            return response;
        return "{}";
    }

    public sealed record class ChatRequest
    {
        public required List<CapturedMessage> Messages { get; init; }
        public ChatOptions? Options { get; init; }
    }

    public sealed record class CapturedMessage
    {
        public required string Role { get; init; }
        public required string Text { get; init; }
    }
}
