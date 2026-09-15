using System.Text;
using ScanFace.Application;
using ScanFace.Domain;
using ScanFace.Infrastructure;

namespace ScanFace.Tests;

public sealed class VaultApplicationServiceTests : IDisposable
{
    private const string MasterPassword = "uma senha mestra forte 2026";
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "ScanFace.Tests", Guid.NewGuid().ToString("N"));
    private readonly VaultSession _session = new();

    [Fact]
    public async Task Vault_UnlocksWithHelloWithoutMasterPassword_AndKeepsSecretsEncryptedAtRest()
    {
        var paths = new AppPaths(_temporaryDirectory);
        var service = CreateService(paths);
        await service.InitializeAsync(MasterPassword, true);
        var entry = new VaultEntry
        {
            Name = "Conta Confidencial Única",
            Username = "usuario-secreto@example.test",
            Password = "SenhaSuperSecreta!2026",
            Website = "https://example.test"
        };
        await service.SaveEntryAsync(entry);

        var databaseBytes = await File.ReadAllBytesAsync(paths.VaultDatabasePath);
        var databaseText = Encoding.UTF8.GetString(databaseBytes);
        Assert.DoesNotContain(entry.Name, databaseText);
        Assert.DoesNotContain(entry.Username, databaseText);
        Assert.DoesNotContain(entry.Password, databaseText);

        var backupPath = Path.Combine(_temporaryDirectory, "vault.scanface");
        await service.ExportBackupAsync(backupPath);
        var backupText = await File.ReadAllTextAsync(backupPath);
        Assert.DoesNotContain(entry.Name, backupText);
        Assert.DoesNotContain(entry.Password, backupText);

        service.Lock();
        Assert.True(await service.UnlockWithWindowsHelloAsync());
        var restored = Assert.Single(await service.GetEntriesAsync());
        Assert.Equal(entry.Password, restored.Password);
    }

    [Fact]
    public async Task Vault_RejectsWrongMasterPassword()
    {
        var service = CreateService(new AppPaths(_temporaryDirectory));
        await service.InitializeAsync(MasterPassword, false);
        service.Lock();

        Assert.False(await service.UnlockWithMasterPasswordAsync("senha completamente incorreta"));
        Assert.True(await service.UnlockWithMasterPasswordAsync(MasterPassword));
    }

    [Fact]
    public async Task Vault_EncryptsFolders_IncludesThemInBackups_AndPreservesEntriesWhenDeleting()
    {
        var paths = new AppPaths(_temporaryDirectory);
        var service = CreateService(paths);
        await service.InitializeAsync(MasterPassword, false);
        var folder = new VaultFolder { Name = "Clientes ultrassecretos" };
        await service.SaveFolderAsync(folder);
        var duplicateError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveFolderAsync(new VaultFolder { Name = folder.Name.ToUpperInvariant() }));
        Assert.Equal("Já existe uma pasta com esse nome.", duplicateError.Message);
        var entry = new VaultEntry
        {
            Name = "Portal reservado",
            Username = "contato@example.test",
            Password = "SenhaReservada!2026",
            FolderId = folder.Id
        };
        await service.SaveEntryAsync(entry);

        var restoredFolder = Assert.Single(await service.GetFoldersAsync());
        var restoredEntry = Assert.Single(await service.GetEntriesAsync());
        Assert.Equal(folder.Name, restoredFolder.Name);
        Assert.Equal(folder.Id, restoredEntry.FolderId);

        var databaseText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(paths.VaultDatabasePath));
        Assert.DoesNotContain(folder.Name, databaseText);

        var backupPath = Path.Combine(_temporaryDirectory, "folders.scanface");
        await service.ExportBackupAsync(backupPath);
        var backupText = await File.ReadAllTextAsync(backupPath);
        Assert.DoesNotContain(folder.Name, backupText);
        Assert.DoesNotContain(entry.Name, backupText);

        service.Lock();
        var importedService = CreateService(new AppPaths(Path.Combine(_temporaryDirectory, "restored")));
        await importedService.ImportBackupAsync(backupPath, MasterPassword, false);
        Assert.Equal(folder.Name, Assert.Single(await importedService.GetFoldersAsync()).Name);
        Assert.Equal(folder.Id, Assert.Single(await importedService.GetEntriesAsync()).FolderId);

        await importedService.DeleteFolderAsync(folder.Id);

        Assert.Empty(await importedService.GetFoldersAsync());
        Assert.Null(Assert.Single(await importedService.GetEntriesAsync()).FolderId);
    }

    [Fact]
    public async Task Vault_AddsFolderStorageWhenOpeningAnExistingDatabase()
    {
        var paths = new AppPaths(_temporaryDirectory);
        var service = CreateService(paths);
        await service.InitializeAsync(MasterPassword, false);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                         $"Data Source={paths.VaultDatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE folders";
            await command.ExecuteNonQueryAsync();
        }

        Assert.Empty(await service.GetFoldersAsync());
        var folder = new VaultFolder { Name = "Pasta migrada" };
        await service.SaveFolderAsync(folder);
        Assert.Equal(folder.Name, Assert.Single(await service.GetFoldersAsync()).Name);
    }

    private VaultApplicationService CreateService(AppPaths paths)
    {
        var repository = new SqliteVaultRepository(paths);
        return new VaultApplicationService(
            repository,
            new AesGcmCryptographyService(),
            new TestDeviceKeyProtector(),
            new TestHelloService(),
            new JsonBackupFileService(),
            _session);
    }

    public void Dispose()
    {
        _session.Dispose();
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }

    private sealed class TestHelloService : IWindowsHelloService
    {
        public Task<bool> IsAvailableAsync() => Task.FromResult(true);
        public Task<bool> RequestVerificationAsync(string message) => Task.FromResult(true);
    }

    private sealed class TestDeviceKeyProtector : IDeviceKeyProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Select(value => (byte)(value ^ 0xA5)).ToArray();
        public byte[] Unprotect(byte[] protectedData) => Protect(protectedData);
    }
}
