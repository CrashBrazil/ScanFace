using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

public sealed record WifiSubnet(IPAddress Address, int PrefixLength, int InterfaceIndex)
{
    private uint Mask => uint.MaxValue << (32 - PrefixLength);
    private uint Network => ToUInt32(Address) & Mask;

    public IPAddress Broadcast
    {
        get
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, Network | ~Mask);
            return new IPAddress(bytes);
        }
    }

    public bool Contains(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork &&
        (ToUInt32(address) & Mask) == Network &&
        ToUInt32(address) != Network && ToUInt32(address) != (Network | ~Mask);

    private static uint ToUInt32(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    public static WifiSubnet? Current()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                  adapter.OperationalStatus == OperationalStatus.Up)
                .Select(adapter => adapter.GetIPProperties())
                .SelectMany(properties => properties.UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                                      address.PrefixLength is >= 8 and <= 30)
                    .Select(address => new WifiSubnet(address.Address, address.PrefixLength, properties.GetIPv4Properties().Index)))
                .FirstOrDefault();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}

public sealed record LanVaultOffer(Guid Id, string Name, string Address, int Port, string Fingerprint, LanAccess Access, string? InvitationSecret)
{
    private const string Prefix = "scanface-lan-v1:";

    public string VerificationCode => Fingerprint.Length >= 20 ? Fingerprint[..20] : string.Empty;

    public string ToInvitation() => Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, ExternalVaultFiles.Json));

    public static LanVaultOffer FromInvitation(string text)
    {
        text = text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal) || text.Length > 2_048)
        {
            throw new FormatException("Convite de rede inválido.");
        }
        return Check(ExternalVaultFiles.ReadJson<LanVaultOffer>(Convert.FromBase64String(text[Prefix.Length..])));
    }

    internal static LanVaultOffer Check(LanVaultOffer offer)
    {
        if (offer.Id == Guid.Empty || offer.Name is null || ExternalVaultContent.ValidateName(offer.Name) != offer.Name ||
            !IPAddress.TryParse(offer.Address, out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
            offer.Port is < 1 or > 65_535 || offer.Fingerprint is null || offer.Fingerprint.Length != 64 ||
            !offer.Fingerprint.All(char.IsAsciiHexDigit) || !Enum.IsDefined(offer.Access) ||
            (offer.InvitationSecret is not null && (offer.InvitationSecret.Length != 64 || !offer.InvitationSecret.All(char.IsAsciiHexDigit))))
        {
            throw new InvalidDataException("Convite de rede inválido.");
        }
        return offer;
    }
}

public static class LanVaultPolicy
{
    public static async Task<LanVaultSettings> CreateAsync(ICryptographyService crypto, bool visible, LanAccess access,
        int duration, string login, string password, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(access) || duration is not (0 or 15 or 60))
        {
            throw new ArgumentException("Escolha um modo de acesso e uma duração válidos.");
        }
        login = login.Trim();
        if (access == LanAccess.LoginAndPassword && (login.Length is < 1 or > 100 || login.Any(char.IsControl)))
        {
            throw new ArgumentException("Informe um login de até 100 caracteres.");
        }
        if (access == LanAccess.Network)
        {
            return new(visible, access, duration, string.Empty, [], []);
        }
        if (password.Length is < 12 or > 256 || string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Use uma senha com 12 a 256 caracteres.");
        }
        var salt = RandomNumberGenerator.GetBytes(16);
        var verifier = await crypto.DeriveKeyAsync(password, salt, Argon2Parameters.DesktopDefault, cancellationToken);
        return new(visible, access, duration, access == LanAccess.LoginAndPassword ? login : string.Empty, salt, verifier);
    }

    internal static async Task<bool> AuthorizeAsync(ICryptographyService crypto, LanVaultSettings settings,
        string? login, string? password, CancellationToken cancellationToken)
    {
        if (settings.Access == LanAccess.Network)
        {
            return true;
        }
        if (password is null || password.Length is < 1 or > 256 || string.IsNullOrWhiteSpace(password) ||
            (settings.Access == LanAccess.LoginAndPassword && !string.Equals(login, settings.Login, StringComparison.Ordinal)))
        {
            return false;
        }
        var hash = await crypto.DeriveKeyAsync(password, settings.Salt, Argon2Parameters.DesktopDefault, cancellationToken);
        try
        {
            return CryptographicOperations.FixedTimeEquals(hash, settings.Verifier);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }
}

internal sealed record LanRequest(Guid Id, string? Secret, string? Login, string? Password);
internal sealed record LanReply(ExternalVaultContent? Content);

internal static class LanFrame
{
    internal static async Task WriteAsync<T>(Stream stream, T message, int maxBytes, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, ExternalVaultFiles.Json);
        try
        {
            if (bytes.Length > maxBytes)
            {
                throw new InvalidDataException("O conteúdo é grande demais para enviar.");
            }
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            await stream.WriteAsync(length, cancellationToken);
            await stream.WriteAsync(bytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    internal static async Task<T> ReadAsync<T>(Stream stream, int maxBytes, CancellationToken cancellationToken) where T : class
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > maxBytes)
        {
            throw new InvalidDataException("Mensagem de rede inválida.");
        }
        var bytes = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            return ExternalVaultFiles.ReadJson<T>(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
