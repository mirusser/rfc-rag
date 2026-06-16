using RfcRag.Settings;

namespace RfcRag.Tests.UnitTests;

public sealed class RfcRagOptionsValidatorTests
{
    private static RfcRagOptions ValidOptions => new()
    {
        RfcMirrorPath = "/rfc",
        PostgresConnectionString = "Host=localhost;Database=rfcrag",
        EmbeddingModel = "openai/text-embedding-3-small",
        EmbeddingBatchSize = 20,
        EmbeddingDimensions = 1536,
        MaxIndexingParallelism = 4,
        MaxEmbeddingConcurrency = 2,
        OpenRouterEmbeddingEndpoint = "https://openrouter.ai/api/v1",
        LocalEmbeddingEndpoint = "http://localhost:11434/v1"
    };

    private readonly RfcRagOptionsValidator validator = new();

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var result = validator.Validate(null, ValidOptions);
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyRfcMirrorPath_Fails(string value)
    {
        var options = ValidOptions with { RfcMirrorPath = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.RfcMirrorPath)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyPostgresConnectionString_Fails(string value)
    {
        var options = ValidOptions with { PostgresConnectionString = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.PostgresConnectionString)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyEmbeddingModel_Fails(string value)
    {
        var options = ValidOptions with { EmbeddingModel = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.EmbeddingModel)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2049)]
    public void Validate_InvalidEmbeddingBatchSize_Fails(int value)
    {
        var options = ValidOptions with { EmbeddingBatchSize = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.EmbeddingBatchSize)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2048)]
    public void Validate_ValidEmbeddingBatchSize_Succeeds(int value)
    {
        var options = ValidOptions with { EmbeddingBatchSize = value };
        var result = validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16001)]
    public void Validate_InvalidEmbeddingDimensions_Fails(int value)
    {
        var options = ValidOptions with { EmbeddingDimensions = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.EmbeddingDimensions)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_InvalidMaxIndexingParallelism_Fails(int value)
    {
        var options = ValidOptions with { MaxIndexingParallelism = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.MaxIndexingParallelism)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidMaxEmbeddingConcurrency_Fails(int value)
    {
        var options = ValidOptions with { MaxEmbeddingConcurrency = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.MaxEmbeddingConcurrency)));
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("ftp://example.com")]
    [InlineData("")]
    public void Validate_InvalidOpenRouterEmbeddingEndpoint_Fails(string value)
    {
        var options = ValidOptions with { OpenRouterEmbeddingEndpoint = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.OpenRouterEmbeddingEndpoint)));
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("ftp://example.com")]
    [InlineData("")]
    public void Validate_InvalidLocalEmbeddingEndpoint_Fails(string value)
    {
        var options = ValidOptions with { LocalEmbeddingEndpoint = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.LocalEmbeddingEndpoint)));
    }

    [Fact]
    public void Validate_MultipleViolations_ReportsAll()
    {
        var options = ValidOptions with
        {
            RfcMirrorPath = "",
            EmbeddingBatchSize = 0,
            MaxIndexingParallelism = 0
        };

        var result = validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.True(result.Failures!.Skip(2).Any());
    }

    [Fact]
    public void Validate_ChatModelNull_GenerationDisabled_Succeeds()
    {
        var options = ValidOptions with { ChatModel = null };
        var result = validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ChatModelSet_Valid_Succeeds()
    {
        var options = ValidOptions with { ChatModel = "openai/gpt-4o-mini" };
        var result = validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ChatModelSetButEmpty_Fails(string value)
    {
        var options = ValidOptions with { ChatModel = value };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.ChatModel)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidMaxAnswerTokens_Fails(int value)
    {
        var options = ValidOptions with
        {
            ChatModel = "openai/gpt-4o-mini",
            MaxAnswerTokens = value
        };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.MaxAnswerTokens)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_InvalidEvidenceBudgetChars_Fails(int value)
    {
        var options = ValidOptions with
        {
            ChatModel = "openai/gpt-4o-mini",
            EvidenceBudgetChars = value
        };
        var result = validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(RfcRagOptions.EvidenceBudgetChars)));
    }

    [Fact]
    public void Validate_MultipleChatViolations_ReportsAll()
    {
        var options = ValidOptions with
        {
            ChatModel = "",
            MaxAnswerTokens = 0,
            EvidenceBudgetChars = 0
        };

        var result = validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.True(result.Failures!.Skip(2).Any());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_VectorDataSearchEnabled_BothValues_Succeed(bool value)
    {
        var options = ValidOptions with { VectorDataSearchEnabled = value };
        var result = validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }
}
