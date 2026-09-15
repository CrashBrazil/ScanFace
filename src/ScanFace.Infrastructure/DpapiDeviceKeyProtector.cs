using System.Security.Cryptography;
using System.Text;
using ScanFace.Application;

namespace ScanFace.Infrastructure;

public sealed class DpapiDeviceKeyProtector : IDeviceKeyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ScanFace:WindowsHello:v1");

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData) =>
        ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}
