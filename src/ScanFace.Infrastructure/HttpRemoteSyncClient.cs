using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

public sealed class HttpRemoteSyncClient : IRemoteSyncClient
{
    private readonly HttpClient _httpClient;

    public HttpRemoteSyncClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<RemoteVaultDocument?> GetAsync(
        SyncSettings settings,
        Guid vaultId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, settings, vaultId);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RemoteVaultDocument>(cancellationToken)
            ?? throw new InvalidDataException("A API retornou uma resposta vazia.");
    }

    public async Task<RemoteVaultDocument> PutAsync(
        SyncSettings settings,
        Guid vaultId,
        string payload,
        long? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Put, settings, vaultId);
        request.Content = JsonContent.Create(new PutVaultRequest(payload));
        if (expectedVersion.HasValue)
        {
            request.Headers.TryAddWithoutValidation("If-Match", expectedVersion.Value.ToString());
        }
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new SyncConflictException("O cofre remoto mudou durante a sincronização.");
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RemoteVaultDocument>(cancellationToken)
            ?? throw new InvalidDataException("A API retornou uma resposta vazia.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, SyncSettings settings, Guid vaultId)
    {
        if (!Uri.TryCreate(settings.ServerUrl.TrimEnd('/') + $"/api/v1/vaults/{vaultId:D}", UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("A URL do servidor de sincronização é inválida.");
        }
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-ScanFace-Token", settings.ApiToken);
        return request;
    }

    private sealed record PutVaultRequest(string Payload);
}

public sealed class SyncConflictException(string message) : Exception(message);
