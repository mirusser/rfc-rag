using RfcRag.Settings;

namespace RfcRag.Tests.UnitTests;

public sealed class RfcRagOptionsTests
{
    [Fact]
    public void EmbeddingDimensions_DefaultValue_Is1536()
    {
        var options = new RfcRagOptions
        {
            RfcMirrorPath = string.Empty,
            PostgresConnectionString = string.Empty,
        };

        Assert.Equal(1536, options.EmbeddingDimensions);
    }

    [Fact]
    public void EmbeddingProvider_DefaultValue_IsOpenRouter()
    {
        var options = new RfcRagOptions
        {
            RfcMirrorPath = string.Empty,
            PostgresConnectionString = string.Empty,
        };

        Assert.Equal(EmbeddingProvider.OpenRouter, options.EmbeddingProvider);
    }

    [Fact]
    public void LocalEmbeddingEndpoint_DefaultValue_IsOllamaLocalhost()
    {
        var options = new RfcRagOptions
        {
            RfcMirrorPath = string.Empty,
            PostgresConnectionString = string.Empty,
        };

        Assert.Equal("http://localhost:11434/v1", options.LocalEmbeddingEndpoint);
    }

    [Fact]
    public void QueryPlannerEnabled_DefaultValue_IsTrue()
    {
        var options = new RfcRagOptions
        {
            RfcMirrorPath = string.Empty,
            PostgresConnectionString = string.Empty,
        };

        Assert.True(options.QueryPlannerEnabled);
    }

    [Fact]
    public void VectorDataSearchEnabled_DefaultValue_IsFalse()
    {
        var options = new RfcRagOptions
        {
            RfcMirrorPath = string.Empty,
            PostgresConnectionString = string.Empty,
        };

        Assert.False(options.VectorDataSearchEnabled);
    }
}
