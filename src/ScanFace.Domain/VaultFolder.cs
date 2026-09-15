namespace ScanFace.Domain;

public sealed class VaultFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public VaultFolder Copy() => new()
    {
        Id = Id,
        Name = Name,
        CreatedAtUtc = CreatedAtUtc,
        UpdatedAtUtc = UpdatedAtUtc
    };
}
