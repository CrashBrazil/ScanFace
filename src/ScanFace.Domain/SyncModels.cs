namespace ScanFace.Domain;

public sealed class PortableVaultSnapshot
{
    public required VaultMetadata Metadata { get; init; }
    public required IReadOnlyList<StoredEncryptedEntry> Entries { get; init; }
    public DateTimeOffset ExportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class SyncSettings
{
    public string ServerUrl { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public long? LastRemoteVersion { get; set; }
    public DateTimeOffset? LastSyncUtc { get; set; }
}

public sealed record RemoteVaultDocument(
    Guid VaultId,
    long Version,
    DateTimeOffset UpdatedAtUtc,
    string Payload);

public enum SyncStatus
{
    Disabled,
    UpToDate,
    Uploaded,
    Downloaded,
    Conflict
}

public sealed record SyncResult(SyncStatus Status, string Message);
