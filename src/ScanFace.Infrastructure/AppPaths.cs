namespace ScanFace.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScanFace");
    }

    public string RootDirectory { get; }
    public string VaultDatabasePath => Path.Combine(RootDirectory, "vault.db");
    public string SyncSettingsPath => Path.Combine(RootDirectory, "sync.settings");
    public string ErrorLogPath => Path.Combine(RootDirectory, "error.log");

    public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);
}
