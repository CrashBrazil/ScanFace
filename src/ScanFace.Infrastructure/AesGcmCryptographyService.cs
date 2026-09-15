using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

public sealed class AesGcmCryptographyService : ICryptographyService
{
    public byte[] GenerateRandomBytes(int length) => RandomNumberGenerator.GetBytes(length);

    public async Task<byte[]> DeriveKeyAsync(
        string password,
        byte[] salt,
        Argon2Parameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(salt);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = parameters.MemorySizeKb,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.DegreeOfParallelism
            };
            cancellationToken.ThrowIfCancellationRequested();
            return await argon2.GetBytesAsync(32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public EncryptedPayload Encrypt(byte[] plaintext, byte[] key, byte[] associatedData)
    {
        ValidateKey(key);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return new EncryptedPayload(nonce, ciphertext, tag);
    }

    public byte[] Decrypt(EncryptedPayload payload, byte[] key, byte[] associatedData)
    {
        ValidateKey(key);
        if (payload.Nonce.Length != 12 || payload.Tag.Length != 16)
        {
            throw new CryptographicException("Envelope criptográfico inválido.");
        }

        var plaintext = new byte[payload.Ciphertext.Length];
        using var aes = new AesGcm(key, payload.Tag.Length);
        aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext, associatedData);
        return plaintext;
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new CryptographicException("A chave AES deve ter 256 bits.");
        }
    }
}
