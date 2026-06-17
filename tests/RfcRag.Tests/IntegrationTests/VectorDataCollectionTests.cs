using Dapper;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.PgVector;
using Npgsql;
using RfcRag.Search;

namespace RfcRag.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class VectorDataCollectionTests : IClassFixture<MediumCorpusFixture>
{
    readonly MediumCorpusFixture fixture;

    public VectorDataCollectionTests(MediumCorpusFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task GetAsync_ExistingSection_MapsAllFields()
    {
        var ct = TestContext.Current.CancellationToken;

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(fixture.ConnectionString);
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();

        using var collection = new PostgresCollection<Guid, RfcSectionRecord>(
            dataSource,
            "rfc_sections",
            ownsDataSource: false,
            new PostgresCollectionOptions { Schema = "rfc_rag" });

        Guid sectionId;
        await using (var connection = await dataSource.OpenConnectionAsync(ct))
        {
            sectionId = await connection.ExecuteScalarAsync<Guid>(
                new CommandDefinition("SELECT id FROM rfc_rag.rfc_sections LIMIT 1", cancellationToken: ct));
        }

        var record = await collection.GetAsync(
            sectionId,
            new RecordRetrievalOptions { IncludeVectors = true },
            ct);

        Assert.NotNull(record);
        Assert.Equal(sectionId, record.Id);
        Assert.True(record.RfcNumber > 0);
        Assert.NotEmpty(record.Title);
        Assert.NotEmpty(record.Section);
        Assert.NotEmpty(record.Text);
        Assert.NotEmpty(record.SourcePath);
        Assert.NotNull(record.Embedding);
        Assert.Equal(1536, record.Embedding.Value.Length);
    }
}
