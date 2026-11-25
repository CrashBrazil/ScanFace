using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

public sealed class LanVaultHost : IDisposable
{
    private readonly WifiSubnet _network;
    private readonly ICryptographyService _crypto;
    private readonly LanVaultSettings _settings;
    private readonly X509Certificate2 _certificate;
    private readonly SslStreamCertificateContext _certificateContext;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _connections = new(2);
    private readonly Dictionary<IPAddress, int> _attempts = [];
    private readonly Func<WifiSubnet?> _activeNetwork;
    private readonly string? _secret;
    private readonly DateTimeOffset? _expiry;
    private ExternalVaultContent? _content;
    private DateTimeOffset _attemptWindow = DateTimeOffset.UtcNow;
    private bool _disposed;

    public LanVaultHost(WifiSubnet network, HostedVault vault, ICryptographyService crypto,
        Func<WifiSubnet?>? activeNetwork = null)
    {
        _network = network;
        _activeNetwork = activeNetwork ?? WifiSubnet.Current;
        _crypto = crypto;
        _content = vault.Content;
        _settings = vault.Settings;
        _expiry = _settings.DurationMinutes == 0 ? null : DateTimeOffset.UtcNow.AddMinutes(_settings.DurationMinutes);
        _secret = _settings.Discoverable ? null : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=ScanFace LAN", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, critical: false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(24));
        // Schannel requires access to a user key container. Without PersistKeySet the imported key is cleaned up with the certificate.
        var archivePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var archive = generated.Export(X509ContentType.Pkcs12, archivePassword);
        try
        {
            _certificate = X509CertificateLoader.LoadPkcs12(archive, archivePassword, X509KeyStorageFlags.UserKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(archive);
        }
        _certificateContext = SslStreamCertificateContext.Create(_certificate, null, offline: true);

        _listener = new TcpListener(network.Address, 0);
        try
        {
            _listener.Start(8);
        }
        catch
        {
            _certificate.Dispose();
            _lifetime.Dispose();
            throw;
        }
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Fingerprint = _certificate.GetCertHashString(HashAlgorithmName.SHA256);
        _ = ServeAsync(_lifetime.Token);
        if (_settings.Discoverable)
        {
            _ = AnnounceAsync(_lifetime.Token);
        }
    }

    public int Port { get; }
    public string Fingerprint { get; }
    public bool IsActive => !_disposed && (_expiry is null || _expiry > DateTimeOffset.UtcNow) && _activeNetwork() == _network;

    public LanVaultOffer Offer
    {
        get
        {
            var content = Volatile.Read(ref _content) ?? throw new ObjectDisposedException(nameof(LanVaultHost));
            return new(content.Id, content.Name, _network.Address.ToString(), Port, Fingerprint, _settings.Access, _secret);
        }
    }

    public void UpdateContent(ExternalVaultContent content) => Volatile.Write(ref _content, content);

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                if (client.Client.RemoteEndPoint is not IPEndPoint peer || !_network.Contains(peer.Address) ||
                    !IsActive || !_connections.Wait(0))
                {
                    client.Dispose();
                    continue;
                }
                _ = ServeClientAsync(client, peer.Address, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ServeClientAsync(TcpClient client, IPAddress address, CancellationToken cancellationToken)
    {
        using var connection = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var stream = new SslStream(client.GetStream());
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificateContext = _certificateContext,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                AllowRenegotiation = false
            }, timeout.Token);
            var request = await LanFrame.ReadAsync<LanRequest>(stream, 4_096, timeout.Token);
            var content = Volatile.Read(ref _content);
            var allowed = content is not null && request.Id == content.Id && SecretMatches(request.Secret) &&
                          (_settings.Access == LanAccess.Network || TrackAttempt(address)) &&
                          await LanVaultPolicy.AuthorizeAsync(_crypto, _settings, request.Login, request.Password, timeout.Token);
            await LanFrame.WriteAsync(stream, new LanReply(allowed && IsActive ? Volatile.Read(ref _content) : null),
                ExternalVaultFiles.MaxBytes, timeout.Token);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or SocketException or AuthenticationException or
                                      OperationCanceledException or JsonException or ObjectDisposedException) { }
        finally
        {
            _connections.Release();
        }
    }

    private bool SecretMatches(string? candidate) => _secret is null ||
        (candidate?.Length == _secret.Length &&
         CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(_secret), Encoding.ASCII.GetBytes(candidate)));

    private bool TrackAttempt(IPAddress address)
    {
        lock (_attempts)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _attemptWindow >= TimeSpan.FromMinutes(1))
            {
                _attemptWindow = now;
                _attempts.Clear();
            }
            var previous = _attempts.GetValueOrDefault(address);
            if (previous >= 8 || (previous == 0 && _attempts.Count >= 128))
            {
                return false;
            }
            _attempts[address] = previous + 1;
            return true;
        }
    }

    private async Task AnnounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var sender = new UdpClient(new IPEndPoint(_network.Address, 0)) { EnableBroadcast = true, Ttl = 1 };
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            do
            {
                if (!IsActive)
                {
                    return;
                }
                var offer = Offer;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(offer, ExternalVaultFiles.Json);
                await sender.SendAsync(bytes, new IPEndPoint(_network.Broadcast, LanVaultDiscovery.Port), cancellationToken);
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetime.Cancel();
        _listener.Stop();
        _certificate.Dispose();
        Volatile.Write(ref _content, null);
        _lifetime.Dispose();
    }
}
