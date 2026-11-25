using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

public static class LanVaultClient
{
    public static async Task<ExternalVaultContent> OpenAsync(WifiSubnet network, LanVaultOffer offer, string login,
        string password, CancellationToken cancellationToken)
    {
        LanVaultOffer.Check(offer);
        var address = IPAddress.Parse(offer.Address);
        if (!network.Contains(address))
        {
            throw new InvalidOperationException("Conecte-se à mesma rede Wi-Fi do anfitrião.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var client = new TcpClient(new IPEndPoint(network.Address, 0));
        await client.ConnectAsync(address, offer.Port, timeout.Token);
        var expectedPin = Convert.FromHexString(offer.Fingerprint);
        using var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, certificate, _, _) =>
            certificate is not null && CryptographicOperations.FixedTimeEquals(expectedPin, certificate.GetCertHash(HashAlgorithmName.SHA256)));
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "ScanFace LAN",
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            AllowRenegotiation = false,
            CertificateChainPolicy = new X509ChainPolicy { DisableCertificateDownloads = true }
        }, timeout.Token);
        await LanFrame.WriteAsync(stream, new LanRequest(offer.Id, offer.InvitationSecret, login, password), 4_096, timeout.Token);
        var reply = await LanFrame.ReadAsync<LanReply>(stream, ExternalVaultFiles.MaxBytes, timeout.Token);
        if (reply.Content is null || reply.Content.Id != offer.Id)
        {
            throw new UnauthorizedAccessException("Acesso recusado. Confira o convite, o login e a senha.");
        }
        return ExternalVaultFiles.CheckContent(reply.Content);
    }
}

public sealed class LanVaultDiscovery : IDisposable
{
    internal const int Port = 47386;
    private readonly WifiSubnet _network;
    private readonly Socket _socket;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<Guid, (LanVaultOffer Offer, DateTimeOffset Seen)> _offers = new();

    public LanVaultDiscovery(WifiSubnet network, int port = Port)
    {
        _network = network;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch
        {
            _socket.Dispose();
            _lifetime.Dispose();
            throw;
        }
        _ = ListenAsync(_lifetime.Token);
    }

    public WifiSubnet Network => _network;
    public int ListeningPort => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    public IReadOnlyList<LanVaultOffer> VisibleOffers()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-12);
        foreach (var item in _offers)
        {
            if (item.Value.Seen < cutoff)
            {
                ((ICollection<KeyValuePair<Guid, (LanVaultOffer Offer, DateTimeOffset Seen)>>)_offers).Remove(item);
            }
        }
        return _offers.Values.Select(item => item.Offer).ToArray();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[2_048];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SocketReceiveMessageFromResult result;
                try
                {
                    result = await _socket.ReceiveMessageFromAsync(buffer, SocketFlags.None,
                        new IPEndPoint(IPAddress.Any, 0), cancellationToken);
                }
                catch (SocketException error) when (error.SocketErrorCode == SocketError.MessageSize)
                {
                    continue;
                }
                if (result.RemoteEndPoint is not IPEndPoint peer || result.PacketInformation.Interface != _network.InterfaceIndex ||
                    !_network.Contains(peer.Address) || peer.Address.Equals(_network.Address) || result.ReceivedBytes <= 0 ||
                    result.SocketFlags.HasFlag(SocketFlags.Truncated))
                {
                    continue;
                }
                try
                {
                    var parsed = JsonSerializer.Deserialize<LanVaultOffer>(buffer.AsSpan(0, result.ReceivedBytes), ExternalVaultFiles.Json);
                    if (parsed is null || parsed.InvitationSecret is not null)
                    {
                        continue;
                    }
                    var offer = LanVaultOffer.Check(parsed with { Address = peer.Address.ToString() });
                    if (_offers.Count < 128 || _offers.ContainsKey(offer.Id))
                    {
                        _offers[offer.Id] = (offer, DateTimeOffset.UtcNow);
                    }
                }
                catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException or IOException) { }
            }
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _socket.Dispose();
        _lifetime.Dispose();
    }
}
