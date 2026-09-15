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
