namespace RfcRag.Settings;

/// <summary>Selects the embedding provider used for indexing and search.</summary>
public enum EmbeddingProvider
{
    /// <summary>Use OpenRouter's OpenAI-compatible embedding API (default).</summary>
    OpenRouter,

    /// <summary>Use a local OpenAI-compatible embedding server (e.g. Ollama, llama.cpp).</summary>
    Local
}
