using RfcRag.Indexing;
using RfcRag.Infrastructure;
using RfcRag.Parsing;
using RfcRag.Search;
using RfcRag.Settings;
using RfcRag.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace RfcRag.Tests.IntegrationTests;

/// <summary>
/// Shared fixture that indexes ~200 hand-picked RFCs spanning multiple protocol layers, eras,
/// and document categories (STD, BCP, FYI, Experimental, Historic). Uses
/// <see cref="SemanticFakeEmbeddingGenerator"/> for deterministic, vocabulary-aware vectors.
///
/// Use cases:
/// - Content-assertion tests that need a populated corpus (search, ABNF, normative, metadata).
/// - Manifest/incremental tests that need a pre-indexed baseline.
/// - Any integration test that needs a running pgvector container with migrations applied.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MediumCorpusFixture : IAsyncLifetime
{
    private const string PostgresImage = "pgvector/pgvector:pg17";

    // ~200 RFCs: builds on the LiveApi subset and adds historical RFCs and more protocol
    // families. Covers the range from RFC 1 (oldest) through ~9600.
    internal static readonly int[] RfcSubset =
    [
        // ═══ Era: Dawn (1969–1980) ═══
        // The very first RFCs — host-to-host protocols, initial Telnet/FTP specs
        1, 2, 3, 4, 5, 6, 7, 10,
        13, 15, 16, 20, 22, 25, 30, 40, 50, 80, 100, 200,

        // ═══ Era: Early Internet (1980–1990) ═══
        // TCP/IP foundations, email, DNS, TELNET, FTP, and the first host requirements
        768, 772, 791, 792, 793, 821, 822, 854, 862, 868, 894, 951, 959,
        1034, 1035, 1105, 1157,
        // Core host requirements + routing
        1122, 1123, 1519,

        // ═══ Era: Web & Internet Growth (1990–2000) ═══
        // DHCP, NAT, HTTP/1.0/1.1, MIME, HTML, URL, IPSec, SSL/TLS, RIP/OSPF/BGP
        1541, 1542, 1631, 1661, 1812, 1918, 1928, 1939, 1945, 1980, 1994,
        2045, 2046, 2047, 2048, 2049,
        2068, 2104, 2119, 2131, 2132, 2246, 2308, 2328, 2401, 2409, 2459, 2460,
        2535, 2616, 2660, 2671, 2672, 2782, 2818, 2821, 2822, 2865, 2870,
        2930, 2965,

        // ═══ Era: Standards Maturation (2000–2010) ═══
        // SIP, LDAP, Kerberos, XMPP, IPv6, EAP, DNSSEC, STUN, SSH, NTPv4
        3010, 3023, 3261, 3263, 3280, 3315, 3418, 3501, 3536, 3621,
        3645, 3658, 3820, 3875, 3986, 4033, 4034, 4035, 4120, 4210,
        4250, 4251, 4252, 4253, 4254, 4271, 4291, 4301, 4340, 4346,
        4408, 4511, 4512, 4513, 4648, 4760, 4882, 4954, 5000,

        // ═══ Era: Modern (2010–present) ═══
        // TLS 1.2/1.3, HTTP/2, QUIC, COAP, WebSocket, OAuth 2.0, JWT, CBOR
        5155, 5246, 5280, 5321, 5322, 5389, 5452, 5652, 5681, 5766,
        5905, 6066, 6071, 6120, 6121, 6125, 6152, 6201, 6234, 6265,
        6455, 6555, 6604, 6749, 6750, 6844, 6890, 6891, 6901, 6902,
        7230, 7231, 7232, 7233, 7234, 7235, 7251, 7252, 7301,
        7517, 7518, 7519, 7523, 7540, 7541, 7641, 7685,
        7807, 7919, 7934, 7959, 8032, 8075, 8174, 8200, 8259, 8323,
        8414, 8445, 8446, 8555, 8610, 8613, 8693, 8839, 8915,

        // ═══ Era: Recent (2020–present) ═══
        // HTTP semantics (9110 family), QUIC, RFC 9000 series, HTTP/3
        9000, 9001, 9002, 9096, 9110, 9111, 9112, 9113, 9114,
        9220, 9221, 9412, 9420, 9562, 9605, 9620, 9630,
    ];

    private PostgreSqlContainer? container;

    public MediumCorpusFixture()
    {
        RfcMirrorPath = Path.Join(Directory.GetCurrentDirectory(), "TestData");
    }

    public NpgsqlDataSource DataSource { get; private set; } = null!;
    public ISearchService SearchService { get; private set; } = null!;
    internal EmbeddingService EmbeddingService { get; private set; } = null!;
    public string RfcMirrorPath { get; }
    public int IndexedCount { get; private set; }

    /// <summary>
    /// Gets the connection string for the shared pgvector container.
    /// Tests that need to create their own <see cref="NpgsqlDataSource"/> for data
    /// insertion can use this.
    /// </summary>
    public string ConnectionString => container!.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage).Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(container.GetConnectionString());
        dataSourceBuilder.UseVector();
        DataSource = dataSourceBuilder.Build();
        await RfcRagMigrationRunner.ApplyAsync(DataSource, CancellationToken.None);
        await DataSource.ReloadTypesAsync(CancellationToken.None);

        EmbeddingService = new EmbeddingService(
            new SemanticFakeEmbeddingGenerator(),
            new EmbeddingRetryPolicy(TimeProvider.System),
            batchSize: 20,
            embeddingDimensions: 1536,
            maxConcurrency: 1,
            NullLogger<EmbeddingService>.Instance);

        var options = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = RfcMirrorPath,
            PostgresConnectionString = container.GetConnectionString(),
            EmbeddingBatchSize = 20,
            EmbeddingDimensions = 1536,
            EmbeddingProvider = EmbeddingProvider.Local,
            MaxIndexingParallelism = 1,
        });

        var indexer = new RfcIndexer(
            DataSource,
            new IndexingRepository(DataSource),
            new RfcParser(),
            new RfcXmlParser(),
            EmbeddingService,
            options,
            NullLogger<RfcIndexer>.Instance);

        foreach (int rfcNumber in RfcSubset)
        {
            await indexer.IndexSingleAsync(rfcNumber, force: true, CancellationToken.None);
        }

        IndexedCount = await indexer.GetIndexedCountAsync(CancellationToken.None);

        var repository = new SearchRepository(DataSource);
        var metadataRepository = new MetadataRepository(DataSource);
        SearchService = new SearchService(repository, metadataRepository, EmbeddingService, options);
    }

    public async ValueTask DisposeAsync()
    {
        if (DataSource is not null)
            await DataSource.DisposeAsync();
        if (container is not null)
            await container.DisposeAsync();
    }
}
