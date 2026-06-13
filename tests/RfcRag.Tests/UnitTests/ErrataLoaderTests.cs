using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RfcRag.Indexing;
using RfcRag.Models;

namespace RfcRag.Tests.UnitTests;

public sealed class ErrataLoaderTests
{
    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        IReadOnlyList<RfcErratum> result = await ErrataLoader.LoadAsync(
            "/nonexistent/path/errata.json",
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ReturnsEmpty()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "not valid json", TestContext.Current.CancellationToken);
            IReadOnlyList<RfcErratum> result = await ErrataLoader.LoadAsync(
                path,
                NullLogger.Instance,
                TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_NonArrayRoot_ReturnsEmpty()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"object": "not an array"}""", TestContext.Current.CancellationToken);
            IReadOnlyList<RfcErratum> result = await ErrataLoader.LoadAsync(
                path,
                NullLogger.Instance,
                TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
