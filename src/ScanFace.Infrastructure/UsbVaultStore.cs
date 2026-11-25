using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

public sealed class UsbVaultStore(ICryptographyService cryptography, Func<string, UsbVolume?>? locate = null)
{
    public const string FolderName = ".scanface-vault";
    private static readonly Argon2Parameters Derivation = Argon2Parameters.DesktopDefault;
    private readonly Func<string, UsbVolume?> _locate = locate ?? UsbVolumes.Find;

    public static VaultPresence GetPresence(UsbVolume device)
    {
        var directory = Path.Combine(device.Root, FolderName);
        var path = Path.Combine(directory, "vault.v1");
        try
        {
            EnsureRegular(directory);
            EnsureRegular(path);
            return File.Exists(path) ? VaultPresence.Configured : VaultPresence.Available;
        }
        catch (IOException)
        {
            return VaultPresence.Unavailable;
        }
        catch (UnauthorizedAccessException)
        {
            return VaultPresence.Unavailable;
        }
    }

    public bool IsPresent(UsbVolume device) => _locate(device.Root)?.DeviceId == device.DeviceId;

    public async Task<UsbVaultSession> CreateAsync(UsbVolume device, string name, string password, CancellationToken cancellationToken)
    {
        if (password.Length is < 12 or > 256 || string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Use uma senha com 12 a 256 caracteres.");
        }
        var content = ExternalVaultContent.Create(name);
        Resolve(device);
        if (GetPresence(device) != VaultPresence.Available)
        {
            throw new IOException("A unidade já contém um cofre ou está indisponível.");
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var key = await cryptography.DeriveKeyAsync(password, salt, Derivation, cancellationToken);
        try
        {
            var fingerprint = await SaveAsync(device, salt, key, content, null, cancellationToken);
            return new(this, device, content, salt, key, fingerprint);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    public async Task<UsbVaultSession> OpenAsync(UsbVolume device, string password, CancellationToken cancellationToken)
    {
        if (password.Length is < 1 or > 256)
        {
            throw new ArgumentException("Informe a senha do cofre USB.");
        }
        var bytes = await ExternalVaultFiles.ReadAsync(Resolve(device), cancellationToken);
        var envelope = ExternalVaultFiles.ReadJson<UsbEnvelope>(bytes);
        if (envelope.Version != 1 || envelope.Salt?.Length != 16)
        {
            throw new InvalidDataException("Versão do cofre USB inválida.");
        }
        var payload = ExternalVaultFiles.CheckEnvelope(envelope.Payload);

        var key = await cryptography.DeriveKeyAsync(password, envelope.Salt, Derivation, cancellationToken);
        try
        {
            Resolve(device);
            cancellationToken.ThrowIfCancellationRequested();
            var plaintext = cryptography.Decrypt(payload, key, AssociatedData(device));
            try
            {
                var content = ExternalVaultFiles.ReadJson<UsbPayload>(plaintext);
                if (content.DeviceId != device.DeviceId)
                {
                    throw new CryptographicException();
                }
                return new(this, device, ExternalVaultFiles.CheckContent(content.Vault), envelope.Salt, key, SHA256.HashData(bytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("Senha incorreta ou cofre vinculado a outra unidade USB.");
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    internal async Task<byte[]> SaveAsync(UsbVolume device, byte[] salt, byte[] key, ExternalVaultContent content,
        byte[]? previous, CancellationToken cancellationToken)
    {
        ExternalVaultFiles.CheckContent(content);
        var path = Resolve(device);
        var directory = Path.GetDirectoryName(path)!;
        if (previous is null)
        {
            var created = !Directory.Exists(directory);
            Directory.CreateDirectory(directory);
            EnsureRegular(directory);
            if (created)
            {
                File.SetAttributes(directory, File.GetAttributes(directory) | FileAttributes.Hidden);
            }
        }
        else
        {
            var current = await ExternalVaultFiles.ReadAsync(path, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(previous, SHA256.HashData(current)))
            {
                throw new IOException("O cofre USB mudou. Feche e abra novamente para salvar.");
            }
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new UsbPayload(device.DeviceId, content), ExternalVaultFiles.Json);
        try
        {
            var encrypted = cryptography.Encrypt(plaintext, key, AssociatedData(device));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new UsbEnvelope(1, salt, encrypted), ExternalVaultFiles.Json);
            Resolve(device);
            await ExternalVaultFiles.WriteAsync(path, bytes, previous is not null, cancellationToken);
            return SHA256.HashData(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private string Resolve(UsbVolume device)
    {
        if (!IsPresent(device))
        {
            throw new IOException("Conecte novamente a unidade USB original.");
        }
        var directory = Path.Combine(device.Root, FolderName);
        var path = Path.Combine(directory, "vault.v1");
        EnsureRegular(directory);
        EnsureRegular(path);
        return path;
    }

    private static void EnsureRegular(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("Links e pontos de montagem não são aceitos no cofre USB.");
            }
        }
    }

    private static byte[] AssociatedData(UsbVolume device) => Encoding.UTF8.GetBytes($"ScanFace:usb:v1:{device.DeviceId}");
    private sealed record UsbEnvelope(int Version, byte[] Salt, EncryptedPayload Payload);
    private sealed record UsbPayload(string DeviceId, ExternalVaultContent Vault);
}

public enum VaultPresence { Unavailable, Available, Configured }

public sealed class UsbVaultSession : IDisposable
{
    private readonly UsbVaultStore _store;
    private readonly byte[] _key;
    private readonly byte[] _salt;
    private byte[] _fingerprint;
    private bool _disposed;

    internal UsbVaultSession(UsbVaultStore store, UsbVolume device, ExternalVaultContent content, byte[] salt, byte[] key, byte[] fingerprint)
    {
        _store = store;
        Device = device;
        Content = content;
        _salt = salt;
        _key = key;
        _fingerprint = fingerprint;
    }

    public UsbVolume Device { get; }
    public ExternalVaultContent Content { get; private set; }
    public bool IsPresent => !_disposed && _store.IsPresent(Device);

    public async Task SaveAsync(ExternalVaultContent content, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _fingerprint = await _store.SaveAsync(Device, _salt, _key, content, _fingerprint, cancellationToken);
        if (!_disposed)
        {
            Content = content;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
        Content = Content with { Folders = [], Entries = [] };
    }
}
