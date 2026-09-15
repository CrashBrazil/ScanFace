using ScanFace.Domain;

namespace ScanFace.Application;

public interface ICryptographyService
{
    byte[] GenerateRandomBytes(int length);
    Task<byte[]> DeriveKeyAsync(string password, byte[] salt, Argon2Parameters parameters, CancellationToken cancellationToken = default);
    EncryptedPayload Encrypt(byte[] plaintext, byte[] key, byte[] associatedData);
    byte[] Decrypt(EncryptedPayload payload, byte[] key, byte[] associatedData);
}

public interface IDeviceKeyProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}

public interface IWindowsHelloService
{
    Task<bool> IsAvailableAsync();
    Task<bool> RequestVerificationAsync(string message);
}

public interface IVaultRepository
{
    Task<bool> ExistsAsync(CancellationToken cancellationToken = default);
    Task InitializeAsync(VaultMetadata metadata, CancellationToken cancellationToken = default);
    Task<VaultMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default);
    Task UpdateMetadataAsync(VaultMetadata metadata, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredEncryptedEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);
    Task UpsertEntryAsync(StoredEncryptedEntry entry, CancellationToken cancellationToken = default);
    Task DeleteEntryAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredEncryptedFolder>> GetFoldersAsync(CancellationToken cancellationToken = default);
    Task UpsertFolderAsync(StoredEncryptedFolder folder, CancellationToken cancellationToken = default);
    Task DeleteFolderAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PortableVaultSnapshot> ExportSnapshotAsync(CancellationToken cancellationToken = default);
    Task ReplaceFromSnapshotAsync(PortableVaultSnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface IBackupFileService
{
    Task SaveAsync(string path, PortableVaultSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<PortableVaultSnapshot> LoadAsync(string path, CancellationToken cancellationToken = default);
}

public interface ISyncSettingsStore
{
    Task<SyncSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SyncSettings settings, CancellationToken cancellationToken = default);
}

public interface IRemoteSyncClient
{
    Task<RemoteVaultDocument?> GetAsync(SyncSettings settings, Guid vaultId, CancellationToken cancellationToken = default);
    Task<RemoteVaultDocument> PutAsync(SyncSettings settings, Guid vaultId, string payload, long? expectedVersion, CancellationToken cancellationToken = default);
}
