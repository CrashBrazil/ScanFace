using System.Security.Cryptography;
using System.Text;
using ScanFace.Domain;
using ScanFace.Infrastructure;

namespace ScanFace.Tests;

public sealed class CryptographyTests
{
    private readonly AesGcmCryptographyService _cryptography = new();

    [Fact]
    public async Task Argon2id_IsDeterministicAndPasswordSensitive()
    {
        var salt = _cryptography.GenerateRandomBytes(16);
        var parameters = new Argon2Parameters(1_024, 1, 1);

        var first = await _cryptography.DeriveKeyAsync("correct horse battery staple", salt, parameters);
        var second = await _cryptography.DeriveKeyAsync("correct horse battery staple", salt, parameters);
        var wrong = await _cryptography.DeriveKeyAsync("different password", salt, parameters);

        Assert.Equal(first, second);
        Assert.NotEqual(first, wrong);
    }

    [Fact]
    public void AesGcm_RoundTripsAndRejectsTampering()
    {
        var key = _cryptography.GenerateRandomBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("segredo de teste");
        var aad = Encoding.UTF8.GetBytes("record:1");
        var encrypted = _cryptography.Encrypt(plaintext, key, aad);

        Assert.Equal(plaintext, _cryptography.Decrypt(encrypted, key, aad));

        encrypted.Ciphertext[0] ^= 0x01;
        Assert.Throws<AuthenticationTagMismatchException>(() => _cryptography.Decrypt(encrypted, key, aad));
    }
}
