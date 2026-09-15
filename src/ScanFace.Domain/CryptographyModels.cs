namespace ScanFace.Domain;

public sealed record Argon2Parameters(int MemorySizeKb, int Iterations, int DegreeOfParallelism)
{
    public static Argon2Parameters DesktopDefault { get; } = new(65_536, 3, 4);
}

public sealed record EncryptedPayload(byte[] Nonce, byte[] Ciphertext, byte[] Tag);

public sealed record StoredEncryptedEntry(
    Guid Id,
    byte[] Nonce,
    byte[] Ciphertext,
    byte[] Tag,
    DateTimeOffset UpdatedAtUtc);

public sealed class VaultMetadata
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required Guid VaultId { get; init; }
    public required byte[] MasterSalt { get; init; }
    public required Argon2Parameters KeyDerivation { get; init; }
    public required EncryptedPayload MasterKeyEnvelope { get; init; }
    public byte[]? HelloKeyEnvelope { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}
