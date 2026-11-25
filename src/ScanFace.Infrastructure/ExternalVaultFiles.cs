using System.Security.Cryptography;
using System.Text.Json;
using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

internal static class ExternalVaultFiles
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const int MaxBytes = 16 * 1024 * 1024;

    internal static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        if (file.Length is <= 0 or > MaxBytes)
        {
            throw new InvalidDataException("O tamanho do arquivo do cofre é inválido.");
        }
        var bytes = new byte[(int)file.Length];
        await file.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

    internal static async Task WriteAsync(string path, byte[] bytes, bool replace, CancellationToken cancellationToken)
    {
        if (bytes.Length > MaxBytes)
        {
            throw new InvalidDataException("O cofre ultrapassou o limite de 16 MB.");
        }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                await file.WriteAsync(bytes, cancellationToken);
                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, path, replace);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static T ReadJson<T>(byte[] bytes) where T : class =>
        JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Dados do cofre inválidos.");

    internal static EncryptedPayload CheckEnvelope(EncryptedPayload? payload)
    {
        if (payload?.Nonce?.Length != 12 || payload.Tag?.Length != 16 || payload.Ciphertext is not { Length: > 0 and <= MaxBytes })
        {
            throw new InvalidDataException("Envelope do cofre inválido.");
        }
        return payload;
    }

    internal static ExternalVaultContent CheckContent(ExternalVaultContent content)
    {
        if (content.Id == Guid.Empty || content.Name is null || ExternalVaultContent.ValidateName(content.Name) != content.Name ||
            content.Entries is null || content.Folders is null || content.Entries.Count > 5_000 || content.Folders.Count > 1_000)
        {
            throw new InvalidDataException("Conteúdo do cofre inválido.");
        }
        var folders = new HashSet<Guid>();
        foreach (var folder in content.Folders)
        {
            if (folder is null || folder.Id == Guid.Empty || !folders.Add(folder.Id) || string.IsNullOrWhiteSpace(folder.Name) || folder.Name.Length > 80)
            {
                throw new InvalidDataException("O cofre contém uma pasta inválida.");
            }
        }
        var entries = new HashSet<Guid>();
        foreach (var entry in content.Entries)
        {
            if (entry is null || entry.Id == Guid.Empty || !entries.Add(entry.Id) || string.IsNullOrWhiteSpace(entry.Name) ||
                entry.Password is null || entry.Username is null || entry.Website is null || entry.Notes is null ||
                (entry.FolderId is Guid id && !folders.Contains(id)))
            {
                throw new InvalidDataException("O cofre contém uma credencial inválida.");
            }
        }
        return content;
    }
}

public sealed class HostedVaultStore(AppPaths paths, VaultApplicationService personalVault)
{
    private string CatalogPath => Path.Combine(paths.RootDirectory, "external-vaults.v1");
    private readonly SemaphoreSlim _gate = new(1);

    public async Task<IReadOnlyList<HostedVault>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(CatalogPath))
        {
            return [];
        }
        var encoded = await ExternalVaultFiles.ReadAsync(CatalogPath, cancellationToken);
        var payload = ExternalVaultFiles.CheckEnvelope(ExternalVaultFiles.ReadJson<EncryptedPayload>(encoded));
        var plaintext = personalVault.DecryptExternalCatalog(payload);
        try
        {
            return ExternalVaultFiles.ReadJson<List<HostedVault>>(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task SaveAsync(HostedVault hosted, CancellationToken cancellationToken)
    {
        ExternalVaultFiles.CheckContent(hosted.Content);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await LoadAsync(cancellationToken)).Where(item => item.Content.Id != hosted.Content.Id).ToList();
            items.Add(hosted);
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(items, ExternalVaultFiles.Json);
            try
            {
                paths.EnsureCreated();
                var payload = personalVault.EncryptExternalCatalog(plaintext);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, ExternalVaultFiles.Json);
                await ExternalVaultFiles.WriteAsync(CatalogPath, bytes, replace: true, cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
