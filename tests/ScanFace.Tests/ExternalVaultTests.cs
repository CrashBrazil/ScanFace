using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using ScanFace.Application;
using ScanFace.Domain;
using ScanFace.Infrastructure;

namespace ScanFace.Tests;

public sealed class ExternalVaultTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ScanFace.Tests", Guid.NewGuid().ToString("N"));
    private readonly AesGcmCryptographyService _crypto = new();

    [Fact]
    public async Task UsbVault_RequiresItsPasswordAndOriginalDeviceAndRejectsStaleWrites()
    {
        var original = new UsbVolume(Path.Combine(_root, "original"), new string('1', 64), "USB");
        var other = new UsbVolume(Path.Combine(_root, "other"), new string('2', 64), "USB");
        Directory.CreateDirectory(original.Root);
        Directory.CreateDirectory(other.Root);
        var store = new UsbVaultStore(_crypto, root => root == original.Root ? original : other);
        var token = CancellationToken.None;
        const string password = "fixture-password-2026";
        using var usb = await store.CreateAsync(original, "Fixture", password, token);
        using var stale = await store.OpenAsync(original, password, token);
        var entry = new VaultEntry { Name = "Synthetic entry", Password = "synthetic secret" };
        await usb.SaveAsync(usb.Content with { Entries = [entry] }, token);
        await Assert.ThrowsAsync<IOException>(() => stale.SaveAsync(stale.Content, token));
        await Assert.ThrowsAsync<CryptographicException>(() => store.OpenAsync(original, "wrong", token));
        var destination = Directory.CreateDirectory(Path.Combine(other.Root, UsbVaultStore.FolderName)).FullName;
        var originalFile = Path.Combine(original.Root, UsbVaultStore.FolderName, "vault.v1");
        File.Copy(originalFile, Path.Combine(destination, "vault.v1"));
        await Assert.ThrowsAsync<CryptographicException>(() => store.OpenAsync(other, password, token));
        Assert.DoesNotContain(entry.Password, await File.ReadAllTextAsync(originalFile, token));
        using var reopened = await store.OpenAsync(original, password, token);
        Assert.Equal(entry.Password, Assert.Single(reopened.Content.Entries).Password);
    }

    [Fact]
    public async Task LanVault_ValidatesPinInviteAndCredentialsBeforeReturningItems()
    {
        var network = new WifiSubnet(IPAddress.Loopback, 8, 1);
        var token = CancellationToken.None;
        var entry = new VaultEntry { Name = "LAN item", Password = "synthetic secret" };
        var content = ExternalVaultContent.Create("LAN fixture") with { Entries = [entry] };
        var policy = await LanVaultPolicy.CreateAsync(_crypto, false, LanAccess.LoginAndPassword, 15, "qa", "fixture-password-2026", token);
        using var host = new LanVaultHost(network, new(content, policy), _crypto, () => network);
        var offer = LanVaultOffer.FromInvitation(host.Offer.ToInvitation());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            LanVaultClient.OpenAsync(network, offer with { InvitationSecret = null }, "qa", "fixture-password-2026", token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            LanVaultClient.OpenAsync(network, offer, "qa", "wrong", token));
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            LanVaultClient.OpenAsync(network, offer with { Fingerprint = new string('A', 64) }, "qa", "fixture-password-2026", token));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LanVaultClient.OpenAsync(new WifiSubnet(IPAddress.Parse("10.0.0.1"), 24, 2), offer, "qa", "fixture-password-2026", token));
        var opened = await LanVaultClient.OpenAsync(network, offer, "qa", "fixture-password-2026", token);
        Assert.Equal(entry.Password, Assert.Single(opened.Entries).Password);
    }

    [Fact]
    public async Task OpenLanVault_UsesSameEncryptedProtocolWithNoAccountPassword()
    {
        var network = new WifiSubnet(IPAddress.Loopback, 8, 1);
        var token = CancellationToken.None;
        var policy = await LanVaultPolicy.CreateAsync(_crypto, true, LanAccess.Network, 0, "", "", token);
        var content = ExternalVaultContent.Create("Open LAN");
        using var host = new LanVaultHost(network, new(content, policy), _crypto, () => network);
        Assert.Null(host.Offer.InvitationSecret);
        var opened = await LanVaultClient.OpenAsync(network, host.Offer, "", "", token);
        Assert.Equal(content.Id, opened.Id);
    }

    [Fact]
    public async Task Discovery_ListsPublicOffersFromTheSelectedLocalInterface()
    {
        var loopbackIndex = NetworkInterface.GetAllNetworkInterfaces()
            .First(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            .GetIPProperties().GetIPv4Properties().Index;
        var network = new WifiSubnet(IPAddress.Parse("127.0.0.2"), 8, loopbackIndex);
        using var discovery = new LanVaultDiscovery(network, port: 0);
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var destination = new IPEndPoint(IPAddress.Loopback, discovery.ListeningPort);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var open = new LanVaultOffer(Guid.NewGuid(), "Visible", "127.0.0.9", 43000, new string('A', 64), LanAccess.Network, null);
        var hidden = open with { Id = Guid.NewGuid(), Name = "Hidden", InvitationSecret = new string('A', 64) };
        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(hidden, options), destination, deadline.Token);
        await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(open, options), destination, deadline.Token);
        while (discovery.VisibleOffers().Count == 0)
        {
            await Task.Delay(30, deadline.Token);
        }
        var visible = Assert.Single(discovery.VisibleOffers());
        Assert.Equal(open.Id, visible.Id);
        Assert.Equal(IPAddress.Loopback.ToString(), visible.Address);
    }

    [Fact]
    public async Task TransferIntoPersonalVault_MergesFoldersAndDoesNotDuplicateItemsOnRetry()
    {
        var paths = new AppPaths(_root);
        using var session = new VaultSession();
        var vault = new VaultApplicationService(new SqliteVaultRepository(paths), _crypto, new DpapiDeviceKeyProtector(),
            new WindowsHelloService(), new JsonBackupFileService(), session);
        var token = CancellationToken.None;
        await vault.InitializeAsync("fixture-master-password-2026", false, token);
        var folder = new VaultFolder { Name = "Trabalho" };
        var entry = new VaultEntry { Name = "Synthetic", Password = "synthetic secret", FolderId = folder.Id, IsFavorite = true, Notes = "Preserved" };
        var source = ExternalVaultContent.Create("USB fixture") with { Folders = [folder], Entries = [entry] };
        Assert.Equal(1, await vault.CopyIntoPersonalVaultAsync(source, token));
        Assert.Equal(0, await vault.CopyIntoPersonalVaultAsync(source, token));
        var restored = await vault.ReadPersonalItemsAsync(token);
        var transferred = Assert.Single(restored.Entries);
        Assert.Equal(Assert.Single(restored.Folders).Id, transferred.FolderId);
        Assert.NotEqual(entry.Id, transferred.Id);
        Assert.True(transferred.IsFavorite);
        Assert.Equal(entry.Notes, transferred.Notes);

        var hosted = new HostedVault(source, await LanVaultPolicy.CreateAsync(_crypto, true, LanAccess.Network, 0, "", "", token));
        var catalog = new HostedVaultStore(paths, vault);
        await catalog.SaveAsync(hosted, token);
        Assert.Equal(hosted.Content.Id, Assert.Single(await catalog.LoadAsync(token)).Content.Id);
        var file = await File.ReadAllTextAsync(Path.Combine(_root, "external-vaults.v1"), token);
        Assert.DoesNotContain(entry.Password, file);
        vault.Lock();
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.LoadAsync(token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
