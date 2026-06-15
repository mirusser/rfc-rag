using Microsoft.Extensions.AI;

namespace RfcRag.Tests.Fakes;

internal sealed class BatchEmptyEmbeddingGenerator(int dimensions = 1536) : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Lock gate = new();
    private readonly List<int> batchSizes = [];

    public IReadOnlyList<int> BatchSizes
    {
        get
        {
            lock (gate)
            {
                return batchSizes.ToArray();
            }
        }
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<string> inputs = values.ToList();
        lock (gate)
        {
            batchSizes.Add(inputs.Count);
        }

        if (inputs.Count > 1)
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
