using Microsoft.Extensions.AI;

namespace RfcRag.Tests.Fakes;

internal sealed class TransientEmptyEmbeddingGenerator(int dimensions = 1536)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    private int callCount;

    public int CallCount => Volatile.Read(ref callCount);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        int currentCall = Interlocked.Increment(ref callCount);
        List<string> inputs = values.ToList();

        if (currentCall == 1)
        {
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>([]));
        }

        List<Embedding<float>> results = new(inputs.Count);
        for (int i = 0; i < inputs.Count; i++)
        {
            results.Add(new Embedding<float>(new float[dimensions]));
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type? serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
