using System.Security.Cryptography;

namespace ScanFace.Application;

public sealed class VaultSession : IDisposable
{
    private byte[]? _vaultKey;

    public bool IsUnlocked => _vaultKey is not null;
    public DateTimeOffset LastActivityUtc { get; private set; }

    public void Unlock(byte[] vaultKey)
    {
        Lock();
        _vaultKey = vaultKey.ToArray();
        Touch();
    }

    internal byte[] RequireKey()
    {
        if (_vaultKey is null)
        {
            throw new InvalidOperationException("O cofre está bloqueado.");
        }

        Touch();
        return _vaultKey;
    }

    public void Touch() => LastActivityUtc = DateTimeOffset.UtcNow;

    public void Lock()
    {
        if (_vaultKey is not null)
        {
            CryptographicOperations.ZeroMemory(_vaultKey);
            _vaultKey = null;
        }
    }

    public void Dispose() => Lock();
}
