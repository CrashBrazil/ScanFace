namespace ScanFace.Tests;

public sealed class SyncDatabaseTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "ScanFace.SyncApi.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Put_UsesOptimisticConcurrencyAndDoesNotOverwriteNewerData()
    {
        var database = new global::SyncDatabase(Path.Combine(_temporaryDirectory, "sync.db"));
        await database.InitializeAsync();
        var vaultId = Guid.NewGuid();

        var first = await database.PutAsync(vaultId, "encrypted-payload-v1", null);
        var rejected = await database.PutAsync(vaultId, "stale-payload", null);
        var second = await database.PutAsync(vaultId, "encrypted-payload-v2", first!.Version);

        Assert.NotNull(first);
        Assert.Null(rejected);
        Assert.Equal(2, second!.Version);
        Assert.Equal("encrypted-payload-v2", (await database.GetAsync(vaultId))!.Payload);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}
