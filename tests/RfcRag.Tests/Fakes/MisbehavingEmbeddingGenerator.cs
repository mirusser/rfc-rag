using Microsoft.Extensions.AI;

namespace RfcRag.Tests.Fakes;

/// <summary>
/// Returns embeddings with wrong count or wrong dimensions to test validation in EmbeddingService.
/// </summary>
internal sealed class MisbehavingEmbeddingGenerator(int wrongCount = -1, int wrongDimensions = -1)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        int count = wrongCount >= 0 ? wrongCount : list.Count;
        int dims = wrongDimensions >= 0 ? wrongDimensions : 4;

        var results = new List<Embedding<float>>(count);
        for (int i = 0; i < count; i++)
            results.Add(new Embedding<float>(new float[dims]));

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type? serviceType, object? key = null) => null;

    public void Dispose() { }
}
