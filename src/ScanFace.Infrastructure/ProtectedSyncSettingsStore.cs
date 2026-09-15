using System.Text.Json;
using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

public sealed class ProtectedSyncSettingsStore : ISyncSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppPaths _paths;
    private readonly IDeviceKeyProtector _protector;

    public ProtectedSyncSettingsStore(AppPaths paths, IDeviceKeyProtector protector)
    {
        _paths = paths;
        _protector = protector;
    }

    public async Task<SyncSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.SyncSettingsPath))
        {
            return new SyncSettings();
        }

        var protectedBytes = await File.ReadAllBytesAsync(_paths.SyncSettingsPath, cancellationToken);
        var plaintext = _protector.Unprotect(protectedBytes);
        try
        {
            return JsonSerializer.Deserialize<SyncSettings>(plaintext, JsonOptions) ?? new SyncSettings();
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task SaveAsync(SyncSettings settings, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        try
        {
            var protectedBytes = _protector.Protect(plaintext);
            await File.WriteAllBytesAsync(_paths.SyncSettingsPath, protectedBytes, cancellationToken);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
