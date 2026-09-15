using System.Text.Json;
using ScanFace.Domain;

namespace ScanFace.Application;

public sealed class SyncCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IVaultRepository _repository;
    private readonly ISyncSettingsStore _settingsStore;
    private readonly IRemoteSyncClient _remote;
    private readonly VaultSession _session;

    public SyncCoordinator(
        IVaultRepository repository,
        ISyncSettingsStore settingsStore,
        IRemoteSyncClient remote,
        VaultSession session)
    {
        _repository = repository;
        _settingsStore = settingsStore;
        _remote = remote;
        _session = session;
    }

    public Task<SyncSettings> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        _settingsStore.LoadAsync(cancellationToken);

    public Task SaveSettingsAsync(SyncSettings settings, CancellationToken cancellationToken = default) =>
        _settingsStore.SaveAsync(settings, cancellationToken);

    public async Task<SyncResult> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        _session.RequireKey();
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(settings.ApiToken))
        {
            return new SyncResult(SyncStatus.Disabled, "Configure a URL e o token da API para sincronizar.");
        }

        var metadata = await _repository.GetMetadataAsync(cancellationToken)
            ?? throw new InvalidOperationException("Nenhum cofre foi configurado.");
        var remote = await _remote.GetAsync(settings, metadata.VaultId, cancellationToken);

        if (remote is null)
        {
            return await UploadAsync(settings, metadata.VaultId, null, cancellationToken);
        }

        var localChanged = settings.LastSyncUtc is null || metadata.LastModifiedUtc > settings.LastSyncUtc;
        var remoteChanged = settings.LastRemoteVersion is null || remote.Version != settings.LastRemoteVersion;

        if (localChanged && remoteChanged)
        {
            return new SyncResult(
                SyncStatus.Conflict,
                "O cofre local e o remoto têm alterações. Exporte um backup antes de escolher qual versão manter.");
        }

        if (remoteChanged)
        {
            var bytes = Convert.FromBase64String(remote.Payload);
            var snapshot = JsonSerializer.Deserialize<PortableVaultSnapshot>(bytes, JsonOptions)
                ?? throw new InvalidDataException("O conteúdo sincronizado é inválido.");
            if (snapshot.Metadata.VaultId != metadata.VaultId)
            {
                throw new InvalidDataException("O servidor retornou um cofre com identificador diferente.");
            }

            snapshot.Metadata.HelloKeyEnvelope = metadata.HelloKeyEnvelope;
            await _repository.ReplaceFromSnapshotAsync(snapshot, cancellationToken);
            settings.LastRemoteVersion = remote.Version;
            settings.LastSyncUtc = DateTimeOffset.UtcNow;
            await _settingsStore.SaveAsync(settings, cancellationToken);
            return new SyncResult(SyncStatus.Downloaded, "Alterações cifradas recebidas do servidor.");
        }

        if (localChanged)
        {
            return await UploadAsync(settings, metadata.VaultId, remote.Version, cancellationToken);
        }

        return new SyncResult(SyncStatus.UpToDate, "O cofre já está sincronizado.");
    }

    private async Task<SyncResult> UploadAsync(
        SyncSettings settings,
        Guid vaultId,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        var snapshot = await _repository.ExportSnapshotAsync(cancellationToken);
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions));
        var uploaded = await _remote.PutAsync(settings, vaultId, payload, expectedVersion, cancellationToken);
        settings.LastRemoteVersion = uploaded.Version;
        settings.LastSyncUtc = DateTimeOffset.UtcNow;
        await _settingsStore.SaveAsync(settings, cancellationToken);
        return new SyncResult(SyncStatus.Uploaded, "Backup cifrado enviado ao servidor.");
    }
}
