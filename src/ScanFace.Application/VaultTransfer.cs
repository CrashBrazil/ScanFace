using ScanFace.Domain;

namespace ScanFace.Application;

public static class VaultTransfer
{
    public static ExternalVaultContent Merge(ExternalVaultContent source, ExternalVaultContent destination, bool copyName)
    {
        var folders = destination.Folders.ToList();
        var entries = destination.Entries.ToList();
        var folderIds = new Dictionary<Guid, Guid>();
        foreach (var original in source.Folders)
        {
            var folder = folders.FirstOrDefault(item => string.Equals(item.Name, original.Name, StringComparison.OrdinalIgnoreCase));
            if (folder is null)
            {
                folder = new VaultFolder { Name = original.Name };
                folders.Add(folder);
            }
            folderIds[original.Id] = folder.Id;
        }

        foreach (var original in source.Entries)
        {
            var folderId = original.FolderId is Guid id && folderIds.TryGetValue(id, out var mapped) ? (Guid?)mapped : null;
            if (entries.Any(item => item.Name == original.Name && item.Username == original.Username &&
                                    item.Password == original.Password && item.Website == original.Website &&
                                    item.Notes == original.Notes && item.IsFavorite == original.IsFavorite && item.FolderId == folderId))
            {
                continue;
            }
            var copy = original.Copy();
            copy.Id = Guid.NewGuid();
            copy.FolderId = folderId;
            copy.UpdatedAtUtc = DateTimeOffset.UtcNow;
            entries.Add(copy);
        }

        return destination with
        {
            Name = copyName ? source.Name : destination.Name,
            Folders = folders,
            Entries = entries
        };
    }
}
