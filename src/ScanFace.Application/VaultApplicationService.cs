using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScanFace.Domain;

namespace ScanFace.Application;

public sealed class VaultApplicationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IVaultRepository _repository;
    private readonly ICryptographyService _cryptography;
    private readonly IDeviceKeyProtector _deviceKeyProtector;
    private readonly IWindowsHelloService _windowsHello;
    private readonly IBackupFileService _backupFiles;
    private readonly VaultSession _session;

    public VaultApplicationService(
        IVaultRepository repository,
        ICryptographyService cryptography,
        IDeviceKeyProtector deviceKeyProtector,
        IWindowsHelloService windowsHello,
        IBackupFileService backupFiles,
        VaultSession session)
    {
        _repository = repository;
        _cryptography = cryptography;
        _deviceKeyProtector = deviceKeyProtector;
        _windowsHello = windowsHello;
        _backupFiles = backupFiles;
        _session = session;
    }

    public bool IsUnlocked => _session.IsUnlocked;

    public Task<bool> VaultExistsAsync(CancellationToken cancellationToken = default) =>
        _repository.ExistsAsync(cancellationToken);

    public async Task InitializeAsync(string masterPassword, bool enableWindowsHello, CancellationToken cancellationToken = default)
    {
        ValidateMasterPassword(masterPassword);
        if (await _repository.ExistsAsync(cancellationToken))
        {
            throw new InvalidOperationException("Já existe um cofre neste perfil do Windows.");
        }

        var vaultKey = _cryptography.GenerateRandomBytes(32);
        var salt = _cryptography.GenerateRandomBytes(16);
        var parameters = Argon2Parameters.DesktopDefault;
        var wrappingKey = await _cryptography.DeriveKeyAsync(masterPassword, salt, parameters, cancellationToken);

        try
        {
            var masterEnvelope = _cryptography.Encrypt(vaultKey, wrappingKey, MasterEnvelopeAad());
            byte[]? helloEnvelope = null;

            if (enableWindowsHello)
            {
                if (!await _windowsHello.IsAvailableAsync() ||
                    !await _windowsHello.RequestVerificationAsync("Confirme sua identidade para ativar o Windows Hello no ScanFace."))
                {
                    throw new InvalidOperationException("O Windows Hello não pôde confirmar sua identidade.");
                }

                helloEnvelope = _deviceKeyProtector.Protect(vaultKey);
            }

            var metadata = new VaultMetadata
            {
                VaultId = Guid.NewGuid(),
                MasterSalt = salt,
                KeyDerivation = parameters,
                MasterKeyEnvelope = masterEnvelope,
                HelloKeyEnvelope = helloEnvelope
            };

            await _repository.InitializeAsync(metadata, cancellationToken);
            _session.Unlock(vaultKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    public async Task<bool> UnlockWithMasterPasswordAsync(string masterPassword, CancellationToken cancellationToken = default)
    {
        var metadata = await RequireMetadataAsync(cancellationToken);
        var wrappingKey = await _cryptography.DeriveKeyAsync(masterPassword, metadata.MasterSalt, metadata.KeyDerivation, cancellationToken);

        try
        {
            var vaultKey = _cryptography.Decrypt(metadata.MasterKeyEnvelope, wrappingKey, MasterEnvelopeAad());
            try
            {
                _session.Unlock(vaultKey);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(vaultKey);
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    public async Task<bool> UnlockWithWindowsHelloAsync(CancellationToken cancellationToken = default)
    {
        var metadata = await RequireMetadataAsync(cancellationToken);
        if (metadata.HelloKeyEnvelope is null || !await _windowsHello.IsAvailableAsync())
        {
            return false;
        }

        if (!await _windowsHello.RequestVerificationAsync("Desbloqueie seu cofre ScanFace."))
        {
            return false;
        }

        try
        {
            var vaultKey = _deviceKeyProtector.Unprotect(metadata.HelloKeyEnvelope);
            try
            {
                _session.Unlock(vaultKey);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(vaultKey);
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public async Task<bool> IsWindowsHelloEnabledAsync(CancellationToken cancellationToken = default) =>
        (await _repository.GetMetadataAsync(cancellationToken))?.HelloKeyEnvelope is not null;

    public async Task<bool> IsWindowsHelloAvailableAsync() => await _windowsHello.IsAvailableAsync();

    public async Task EnableWindowsHelloAsync(CancellationToken cancellationToken = default)
    {
        var metadata = await RequireMetadataAsync(cancellationToken);
        if (!await _windowsHello.IsAvailableAsync() ||
            !await _windowsHello.RequestVerificationAsync("Confirme sua identidade para ativar o Windows Hello."))
        {
            throw new InvalidOperationException("O Windows Hello não pôde confirmar sua identidade.");
        }

        metadata.HelloKeyEnvelope = _deviceKeyProtector.Protect(_session.RequireKey());
        metadata.LastModifiedUtc = DateTimeOffset.UtcNow;
        await _repository.UpdateMetadataAsync(metadata, cancellationToken);
    }

    public async Task DisableWindowsHelloAsync(CancellationToken cancellationToken = default)
    {
        var metadata = await RequireMetadataAsync(cancellationToken);
        metadata.HelloKeyEnvelope = null;
        metadata.LastModifiedUtc = DateTimeOffset.UtcNow;
        await _repository.UpdateMetadataAsync(metadata, cancellationToken);
    }

    public async Task ChangeMasterPasswordAsync(string newMasterPassword, CancellationToken cancellationToken = default)
    {
        ValidateMasterPassword(newMasterPassword);
        var metadata = await RequireMetadataAsync(cancellationToken);
        var newSalt = _cryptography.GenerateRandomBytes(16);
        var wrappingKey = await _cryptography.DeriveKeyAsync(newMasterPassword, newSalt, Argon2Parameters.DesktopDefault, cancellationToken);
        try
        {
            var replacement = new VaultMetadata
            {
                SchemaVersion = metadata.SchemaVersion,
                VaultId = metadata.VaultId,
                MasterSalt = newSalt,
                KeyDerivation = Argon2Parameters.DesktopDefault,
                MasterKeyEnvelope = _cryptography.Encrypt(_session.RequireKey(), wrappingKey, MasterEnvelopeAad()),
                HelloKeyEnvelope = metadata.HelloKeyEnvelope,
                CreatedAtUtc = metadata.CreatedAtUtc,
                LastModifiedUtc = DateTimeOffset.UtcNow
            };
            await _repository.UpdateMetadataAsync(replacement, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    public async Task<IReadOnlyList<VaultEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var key = _session.RequireKey();
        var stored = await _repository.GetEntriesAsync(cancellationToken);
        var result = new List<VaultEntry>(stored.Count);

        foreach (var item in stored)
        {
            var payload = new EncryptedPayload(item.Nonce, item.Ciphertext, item.Tag);
            var plaintext = _cryptography.Decrypt(payload, key, EntryAad(item.Id));
            try
            {
                var entry = JsonSerializer.Deserialize<VaultEntry>(plaintext, JsonOptions)
                    ?? throw new InvalidDataException($"O item {item.Id} está corrompido.");
                result.Add(entry);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        return result.OrderByDescending(entry => entry.IsFavorite)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task SaveEntryAsync(VaultEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            throw new ArgumentException("Informe um nome para a credencial.", nameof(entry));
        }

        if (entry.Id == Guid.Empty)
        {
            entry.Id = Guid.NewGuid();
        }

        if (entry.FolderId is Guid folderId &&
            !(await GetFoldersAsync(cancellationToken)).Any(folder => folder.Id == folderId))
        {
            throw new ArgumentException("A pasta selecionada não existe mais.", nameof(entry));
        }

        var now = DateTimeOffset.UtcNow;
        if (entry.CreatedAtUtc == default)
        {
            entry.CreatedAtUtc = now;
        }
        entry.UpdatedAtUtc = now;

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        try
        {
            var encrypted = _cryptography.Encrypt(plaintext, _session.RequireKey(), EntryAad(entry.Id));
            await _repository.UpsertEntryAsync(
                new StoredEncryptedEntry(entry.Id, encrypted.Nonce, encrypted.Ciphertext, encrypted.Tag, now),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Task DeleteEntryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _session.RequireKey();
        return _repository.DeleteEntryAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<VaultFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        var key = _session.RequireKey();
        var stored = await _repository.GetFoldersAsync(cancellationToken);
        var result = new List<VaultFolder>(stored.Count);

        foreach (var item in stored)
        {
            var payload = new EncryptedPayload(item.Nonce, item.Ciphertext, item.Tag);
            var plaintext = _cryptography.Decrypt(payload, key, FolderAad(item.Id));
            try
            {
                var folder = JsonSerializer.Deserialize<VaultFolder>(plaintext, JsonOptions)
                    ?? throw new InvalidDataException($"A pasta {item.Id} está corrompida.");
                result.Add(folder);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        return result.OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task SaveFolderAsync(VaultFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var normalizedName = folder.Name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 80)
        {
            throw new ArgumentException("O nome da pasta deve ter entre 1 e 80 caracteres.", nameof(folder));
        }

        var existingFolders = await GetFoldersAsync(cancellationToken);
        if (existingFolders.Any(item =>
                item.Id != folder.Id &&
                string.Equals(item.Name, normalizedName, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new ArgumentException("Já existe uma pasta com esse nome.", nameof(folder));
        }

        if (folder.Id == Guid.Empty)
        {
            folder.Id = Guid.NewGuid();
        }

        var now = DateTimeOffset.UtcNow;
        if (folder.CreatedAtUtc == default)
        {
            folder.CreatedAtUtc = now;
        }
        folder.Name = normalizedName;
        folder.UpdatedAtUtc = now;

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(folder, JsonOptions);
        try
        {
            var encrypted = _cryptography.Encrypt(plaintext, _session.RequireKey(), FolderAad(folder.Id));
            await _repository.UpsertFolderAsync(
                new StoredEncryptedFolder(folder.Id, encrypted.Nonce, encrypted.Ciphertext, encrypted.Tag, now),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task DeleteFolderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _session.RequireKey();
        var entries = await GetEntriesAsync(cancellationToken);
        foreach (var entry in entries.Where(entry => entry.FolderId == id))
        {
            entry.FolderId = null;
            await SaveEntryAsync(entry, cancellationToken);
        }

        await _repository.DeleteFolderAsync(id, cancellationToken);
    }

    public async Task ExportBackupAsync(string path, CancellationToken cancellationToken = default)
    {
        _session.RequireKey();
        await _backupFiles.SaveAsync(path, await _repository.ExportSnapshotAsync(cancellationToken), cancellationToken);
    }

    public async Task ImportBackupAsync(string path, string masterPassword, bool enableWindowsHello, CancellationToken cancellationToken = default)
    {
        var snapshot = await _backupFiles.LoadAsync(path, cancellationToken);
        var wrappingKey = await _cryptography.DeriveKeyAsync(masterPassword, snapshot.Metadata.MasterSalt, snapshot.Metadata.KeyDerivation, cancellationToken);
        byte[] vaultKey;
        try
        {
            vaultKey = _cryptography.Decrypt(snapshot.Metadata.MasterKeyEnvelope, wrappingKey, MasterEnvelopeAad());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }

        try
        {
            snapshot.Metadata.HelloKeyEnvelope = null;
            if (enableWindowsHello)
            {
                if (!await _windowsHello.IsAvailableAsync() ||
                    !await _windowsHello.RequestVerificationAsync("Confirme sua identidade para proteger este cofre com o Windows Hello."))
                {
                    throw new InvalidOperationException("O Windows Hello não pôde confirmar sua identidade.");
                }
                snapshot.Metadata.HelloKeyEnvelope = _deviceKeyProtector.Protect(vaultKey);
            }

            await _repository.ReplaceFromSnapshotAsync(snapshot, cancellationToken);
            _session.Unlock(vaultKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    public void Lock() => _session.Lock();
    public void Touch() => _session.Touch();

    private async Task<VaultMetadata> RequireMetadataAsync(CancellationToken cancellationToken) =>
        await _repository.GetMetadataAsync(cancellationToken)
        ?? throw new InvalidOperationException("Nenhum cofre foi configurado.");

    private static byte[] MasterEnvelopeAad() => Encoding.UTF8.GetBytes("ScanFace:vault-key:v1");
    private static byte[] EntryAad(Guid id) => Encoding.UTF8.GetBytes($"ScanFace:entry:{id:D}:v1");
    private static byte[] FolderAad(Guid id) => Encoding.UTF8.GetBytes($"ScanFace:folder:{id:D}:v1");

    private static void ValidateMasterPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            throw new ArgumentException("A senha mestra deve ter pelo menos 12 caracteres.", nameof(password));
        }
    }
}
