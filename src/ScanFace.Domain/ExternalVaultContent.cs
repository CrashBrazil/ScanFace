namespace ScanFace.Domain;

public sealed record ExternalVaultContent(Guid Id, string Name, IReadOnlyList<VaultFolder> Folders, IReadOnlyList<VaultEntry> Entries)
{
    public static ExternalVaultContent Create(string name) => new(Guid.NewGuid(), ValidateName(name), [], []);

    public static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 64 || name.Any(char.IsControl))
        {
            throw new ArgumentException("Informe um nome de cofre com até 64 caracteres.");
        }
        return name;
    }
}

public enum LanAccess { Network, Password, LoginAndPassword }

public sealed record LanVaultSettings(bool Discoverable, LanAccess Access, int DurationMinutes, string Login, byte[] Salt, byte[] Verifier);

public sealed record HostedVault(ExternalVaultContent Content, LanVaultSettings Settings);
