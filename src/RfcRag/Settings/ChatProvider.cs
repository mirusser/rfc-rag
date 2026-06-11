namespace RfcRag.Settings;

/// <summary>Selects the chat provider used for answer generation.</summary>
public enum ChatProvider
{
    /// <summary>No chat provider configured; answer generation is disabled.</summary>
    None,

    /// <summary>Use OpenRouter's OpenAI-compatible chat API.</summary>
    OpenRouter,

    /// <summary>Use a local OpenAI-compatible chat server (e.g. Ollama, llama.cpp).</summary>
    Local
}

/// <summary>Extension methods for <see cref="ChatProvider"/>.</summary>
internal static class ChatProviderExtensions
{
    /// <summary>Returns <see langword="true"/> when the provider is enabled for answer generation.</summary>
    public static bool IsEnabled(this ChatProvider provider) => provider is not ChatProvider.None;
}
