using System.Text.Json;
using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

public sealed class JsonBackupFileService : IBackupFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task SaveAsync(string path, PortableVaultSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new BackupDocument("scanface-encrypted-backup", 1, snapshot);
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, path, true);
    }

    public async Task<PortableVaultSnapshot> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var document = await JsonSerializer.DeserializeAsync<BackupDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("O backup está vazio ou inválido.");
        if (document.Format != "scanface-encrypted-backup" || document.Version != 1)
        {
            throw new InvalidDataException("Este arquivo não é um backup ScanFace compatível.");
        }
        return document.Snapshot;
    }

    private sealed record BackupDocument(string Format, int Version, PortableVaultSnapshot Snapshot);
}
