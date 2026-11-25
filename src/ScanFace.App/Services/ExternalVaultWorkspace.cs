using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using ScanFace.App.Presentation;
using ScanFace.App.Views;
using ScanFace.Application;
using ScanFace.Domain;
using ScanFace.Infrastructure;

namespace ScanFace.App.Services;

public sealed class ExternalVaultWorkspace : ObservableObject, IDisposable
{
    private readonly VaultApplicationService _personal;
    private readonly ICryptographyService _crypto;
    private readonly HostedVaultStore _catalog;
    private readonly UsbVaultStore _usb;
    private readonly DialogService _dialogs;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<Guid, LanVaultHost> _hosts = [];
    private readonly Dictionary<string, string> _usbNames = [];
    private CancellationTokenSource? _session;
    private LanVaultDiscovery? _discovery;
    private WifiSubnet? _wifi;
    private IReadOnlyList<HostedVault> _hosted = [];
    private IReadOnlyList<UsbVolume> _devices = [];
    private IReadOnlyList<ExternalVaultSource> _sources = [];
    private ExternalVaultWindow? _window;
    private bool _polling;
    private bool _operating;

    public ExternalVaultWorkspace(VaultApplicationService personal, ICryptographyService crypto, HostedVaultStore catalog,
        UsbVaultStore usb, DialogService dialogs)
    {
        _personal = personal;
        _crypto = crypto;
        _catalog = catalog;
        _usb = usb;
        _dialogs = dialogs;
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public IReadOnlyList<ExternalVaultSource> Sources
    {
        get => _sources;
        private set => SetProperty(ref _sources, value);
    }

    public int PersonalRevision { get; private set; }

    public async Task StartAsync()
    {
        Stop();
        _session = new CancellationTokenSource();
        var token = _session.Token;
        try
        {
            var data = await _catalog.LoadAsync(token);
            token.ThrowIfCancellationRequested();
            _hosted = data;
            await PollAsync();
            if (!token.IsCancellationRequested)
            {
                _timer.Start();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (IsExpected(error))
        {
            _dialogs.ShowMessage("O catálogo de cofres de rede não pôde ser carregado.", "Cofres externos", true);
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _session?.Cancel();
        _session?.Dispose();
        _session = null;
        _discovery?.Dispose();
        _discovery = null;
        foreach (var host in _hosts.Values)
        {
            host.Dispose();
        }
        _hosts.Clear();
        _usbNames.Clear();
        _hosted = [];
        _devices = [];
        _wifi = null;
        Sources = [];
    }

    public void Dispose() => Stop();

    private CancellationToken Token => _session?.Token ?? throw new OperationCanceledException();

    private async Task PollAsync()
    {
        if (_polling || _session is null)
        {
            return;
        }
        _polling = true;
        var token = _session.Token;
        try
        {
            var status = await Task.Run(() => (Wifi: WifiSubnet.Current(), Devices: UsbVolumes.Find()), token);
            token.ThrowIfCancellationRequested();
            _devices = status.Devices;
            if (status.Wifi != _wifi)
            {
                _wifi = status.Wifi;
                _discovery?.Dispose();
                _discovery = null;
            }
            if (_discovery is null && _wifi is not null)
            {
                try { _discovery = new(_wifi); }
                catch (SocketException) { }
            }
            foreach (var id in _hosts.Where(item => !item.Value.IsActive).Select(item => item.Key).ToArray())
            {
                StopHost(id);
            }
            UpdateSources();
            _window?.CheckAvailability(_devices, _wifi);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or SocketException or System.Net.NetworkInformation.NetworkInformationException) { }
        finally
        {
            _polling = false;
        }
    }

    private void UpdateSources()
    {
        var next = _hosted.OrderBy(item => item.Content.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new ExternalVaultSource(ExternalVaultKind.Hosted, item.Content.Name,
                _hosts.ContainsKey(item.Content.Id) ? "Compartilhado por Wi-Fi" : "Cofre de rede · só neste PC", HostedId: item.Content.Id))
            .Concat(_devices.Where(device => UsbVaultStore.GetPresence(device) == VaultPresence.Configured)
                .Select(device => new ExternalVaultSource(ExternalVaultKind.Usb, _usbNames.GetValueOrDefault(device.DeviceId, device.DisplayName),
                    $"Cofre USB · {device.Root}", Usb: device)))
            .Concat((_discovery?.VisibleOffers() ?? []).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(offer => new ExternalVaultSource(ExternalVaultKind.Remote, offer.Name, "Disponível na rede", Offer: offer)))
            .ToArray();
        if (!Sources.SequenceEqual(next))
        {
            Sources = next;
        }
    }

    public Task CreateHostedAsync() => RunAsync(async token =>
    {
        var prompt = new ExternalVaultPrompt("Novo cofre de rede",
            "Escolha quem pode abrir e por quanto tempo. Ative o compartilhamento no cofre quando estiver conectado ao Wi-Fi.", "Criar cofre");
        prompt.ConfigureHosted(null);
        if (_dialogs.ShowVaultDialog(prompt) != true)
        {
            return;
        }
        var content = ExternalVaultContent.Create(prompt.ResultName);
        var settings = await LanVaultPolicy.CreateAsync(_crypto, prompt.ResultVisible, prompt.ResultAccess,
            prompt.ResultDuration, prompt.ResultLogin, prompt.TakePassword(), token);
        var hosted = new HostedVault(content, settings);
        await SaveHostedAsync(hosted, token);
        Show(new(this, new(ExternalVaultKind.Hosted, content.Name, "Cofre de rede", HostedId: content.Id), content, _dialogs, token));
    });

    public Task CreateUsbAsync() => RunAsync(async token =>
    {
        await PollAsync();
        var available = _devices.Where(item => UsbVaultStore.GetPresence(item) == VaultPresence.Available).ToArray();
        if (available.Length == 0)
        {
            throw new InvalidOperationException("Conecte uma unidade USB que informe um número de série e esteja disponível para criar o cofre.");
        }
        var prompt = new ExternalVaultPrompt("Configurar cofre no USB",
            "O ScanFace criará uma pasta oculta cifrada na unidade selecionada. Use uma senha de pelo menos 12 caracteres.", "Criar cofre");
        prompt.ConfigureUsb(available);
        if (_dialogs.ShowVaultDialog(prompt) != true)
        {
            return;
        }
        var device = prompt.SelectedChoice<UsbVolume>();
        using var usb = await _usb.CreateAsync(device, prompt.ResultName, prompt.TakePassword(), token);
        token.ThrowIfCancellationRequested();
        _usbNames[device.DeviceId] = usb.Content.Name;
        UpdateSources();
        Show(new(this, new(ExternalVaultKind.Usb, usb.Content.Name, "Cofre USB", Usb: device), usb.Content, _dialogs, token, usb));
    });

    public Task JoinAsync() => RunAsync(async token =>
    {
        var prompt = new ExternalVaultPrompt("Abrir cofre por convite",
            "Cole um convite recebido diretamente do anfitrião do cofre.", "Continuar");
        prompt.ConfigureCode("Convite do ScanFace");
        if (_dialogs.ShowVaultDialog(prompt) == true)
        {
            var offer = LanVaultOffer.FromInvitation(prompt.ResultCode);
            await OpenAsync(new(ExternalVaultKind.Remote, offer.Name, "Cofre de rede", Offer: offer), token, fromInvitation: true);
        }
    });

    public Task OpenAsync(ExternalVaultSource source) => RunAsync(token => OpenAsync(source, token, fromInvitation: false));

    private async Task OpenAsync(ExternalVaultSource source, CancellationToken token, bool fromInvitation)
    {
        using var usb = source.Kind == ExternalVaultKind.Usb ? await UnlockUsbAsync(source.Usb!, token) : null;
        var content = source.Kind switch
        {
            ExternalVaultKind.Usb => usb?.Content,
            ExternalVaultKind.Hosted => Hosted(source.HostedId).Content,
            ExternalVaultKind.Remote => await DownloadAsync(source.Offer!, fromInvitation, token),
            _ => null
        };
        token.ThrowIfCancellationRequested();
        if (content is null)
        {
            return;
        }
        if (usb is not null)
        {
            _usbNames[usb.Device.DeviceId] = content.Name;
            UpdateSources();
        }
        Show(new(this, source, content, _dialogs, token, usb, source.Kind == ExternalVaultKind.Remote ? WifiSubnet.Current() : null));
    }

    private void Show(ExternalVaultWindow window)
    {
        Token.ThrowIfCancellationRequested();
        _window = window;
        try
        {
            _dialogs.ShowVaultDialog(window);
        }
        finally
        {
            _window = null;
            window.ClearSensitiveView();
            UpdateSources();
        }
    }

    private async Task<UsbVaultSession?> UnlockUsbAsync(UsbVolume device, CancellationToken token)
    {
        var prompt = new ExternalVaultPrompt("Desbloquear cofre USB", device.DisplayName, "Desbloquear");
        prompt.ConfigurePassword();
        return _dialogs.ShowVaultDialog(prompt) == true
            ? await _usb.OpenAsync(device, prompt.TakePassword(), token) : null;
    }

    private async Task<ExternalVaultContent?> DownloadAsync(LanVaultOffer offer, bool fromInvitation, CancellationToken token)
    {
        var network = WifiSubnet.Current() ?? throw new InvalidOperationException("Conecte este PC a uma rede Wi-Fi para abrir um cofre de rede.");
        if (!fromInvitation || offer.Access != LanAccess.Network)
        {
            var prompt = new ExternalVaultPrompt($"Abrir {offer.Name}",
                fromInvitation ? "Informe os dados de acesso definidos pelo anfitrião." :
                    "Confira o código com o anfitrião antes de enviar dados de acesso.", "Abrir cofre");
            prompt.ConfigureJoin(offer, fromInvitation);
            if (_dialogs.ShowVaultDialog(prompt) != true)
            {
                return null;
            }
            return await LanVaultClient.OpenAsync(network, offer, prompt.ResultLogin, prompt.TakePassword(), token);
        }
        return await LanVaultClient.OpenAsync(network, offer, string.Empty, string.Empty, token);
    }

    internal HostedVault Hosted(Guid id) => _hosted.Single(item => item.Content.Id == id);
    internal LanVaultOffer? ActiveOffer(Guid id) => _hosts.TryGetValue(id, out var host) && host.IsActive ? host.Offer : null;

    internal void ToggleHost(Guid id)
    {
        Token.ThrowIfCancellationRequested();
        if (_hosts.ContainsKey(id))
        {
            StopHost(id);
        }
        else
        {
            var wifi = WifiSubnet.Current() ?? throw new InvalidOperationException("Conecte este PC ao Wi-Fi para compartilhar o cofre.");
            _hosts.Add(id, new LanVaultHost(wifi, Hosted(id), _crypto));
        }
        UpdateSources();
    }

    private void StopHost(Guid id)
    {
        if (_hosts.Remove(id, out var host))
        {
            host.Dispose();
        }
    }

    internal async Task<ExternalVaultContent> EditHostedSettingsAsync(Guid id, CancellationToken token)
    {
        var current = Hosted(id);
        var prompt = new ExternalVaultPrompt("Configurar cofre de rede",
            "Salvar a configuração encerra a sessão de compartilhamento e invalida os convites anteriores.", "Salvar");
        prompt.ConfigureHosted(current);
        if (_dialogs.ShowVaultDialog(prompt) != true)
        {
            return current.Content;
        }
        var password = prompt.TakePassword();
        var settings = current.Settings;
        if (password.Length == 0 && settings.Access == prompt.ResultAccess && settings.Access != LanAccess.Network)
        {
            if (prompt.ResultAccess == LanAccess.LoginAndPassword && string.IsNullOrWhiteSpace(prompt.ResultLogin))
            {
                throw new ArgumentException("Informe um login.");
            }
            settings = settings with { Login = prompt.ResultLogin, Discoverable = prompt.ResultVisible, DurationMinutes = prompt.ResultDuration };
        }
        else
        {
            settings = await LanVaultPolicy.CreateAsync(_crypto, prompt.ResultVisible, prompt.ResultAccess, prompt.ResultDuration,
                prompt.ResultLogin, password, token);
        }
        StopHost(id);
        var content = current.Content with { Name = ExternalVaultContent.ValidateName(prompt.ResultName) };
        await SaveHostedAsync(new(content, settings), token);
        return content;
    }

    internal async Task SaveContentAsync(ExternalVaultSource source, ExternalVaultContent content, UsbVaultSession? usb, CancellationToken token)
    {
        if (usb is not null)
        {
            await usb.SaveAsync(content, token);
            token.ThrowIfCancellationRequested();
            _usbNames[usb.Device.DeviceId] = content.Name;
            UpdateSources();
            return;
        }
        var hosted = Hosted(source.HostedId) with { Content = content };
        await SaveHostedAsync(hosted, token);
        if (_hosts.TryGetValue(source.HostedId, out var host))
        {
            host.UpdateContent(content);
        }
    }

    private async Task SaveHostedAsync(HostedVault hosted, CancellationToken token)
    {
        await _catalog.SaveAsync(hosted, token);
        token.ThrowIfCancellationRequested();
        _hosted = _hosted.Where(item => item.Content.Id != hosted.Content.Id).Append(hosted).ToArray();
        UpdateSources();
    }

    internal async Task<(ExternalVaultContent Content, string Message)> TransferAsync(ExternalVaultSource current,
        ExternalVaultContent content, UsbVaultSession? usb, bool inbound, CancellationToken token)
    {
        var personal = new ExternalVaultSource(ExternalVaultKind.Personal, "Cofre pessoal", "Neste computador");
        var others = Sources.Where(item => !SameSource(item, current) && (inbound || item.Kind != ExternalVaultKind.Remote)).Prepend(personal);
        var prompt = new ExternalVaultPrompt(inbound ? "Importar de outro cofre" : "Copiar para outro cofre",
            "A cópia inclui credenciais e pastas. Os itens idênticos que já estão no destino são preservados.", inbound ? "Importar" : "Copiar");
        prompt.ConfigureTransfer(others.Select(other => new ExternalVaultPrompt.Option<ExternalVaultSource>($"{other.Label} · {other.Detail}", other)),
            current.Kind == ExternalVaultKind.Hosted, inbound ? "Origem" : "Destino");
        if (_dialogs.ShowVaultDialog(prompt) != true)
        {
            return (content, string.Empty);
        }

        var other = prompt.SelectedChoice<ExternalVaultSource>();
        var target = inbound ? current : other;
        if (target.Kind == ExternalVaultKind.Hosted && Hosted(target.HostedId).Settings.Access == LanAccess.Network &&
            !_dialogs.Confirm("Quem receber acesso a esse cofre de rede poderá ver as credenciais copiadas. Continuar?", "Copiar para cofre sem senha"))
        {
            return (content, string.Empty);
        }
        using var otherUsb = other.Kind == ExternalVaultKind.Usb ? await UnlockUsbAsync(other.Usb!, token) : null;
        var otherContent = other.Kind switch
        {
            ExternalVaultKind.Personal => inbound ? await _personal.ReadPersonalItemsAsync(token) : null,
            ExternalVaultKind.Hosted => Hosted(other.HostedId).Content,
            ExternalVaultKind.Usb => otherUsb?.Content,
            ExternalVaultKind.Remote => await DownloadAsync(other.Offer!, fromInvitation: false, token),
            _ => null
        };
        token.ThrowIfCancellationRequested();
        if (target.Kind == ExternalVaultKind.Personal)
        {
            var added = await _personal.CopyIntoPersonalVaultAsync(content, token);
            PersonalRevision++;
            return (content, $"{added} credencial(is) copiada(s) para o cofre pessoal.");
        }
        if (otherContent is null)
        {
            return (content, string.Empty);
        }
        var source = inbound ? otherContent : content;
        var destination = inbound ? content : otherContent;
        var merged = VaultTransfer.Merge(source, destination, prompt.ResultCopyName && source.Id != Guid.Empty);
        if (prompt.ResultCopyPublishing && current.Kind == ExternalVaultKind.Hosted && other.Kind == ExternalVaultKind.Hosted)
        {
            var sourceSettings = Hosted(source.Id).Settings;
            var destinationSettings = Hosted(destination.Id).Settings;
            StopHost(destination.Id);
            await SaveHostedAsync(new(merged, destinationSettings with
            {
                Discoverable = sourceSettings.Discoverable, DurationMinutes = sourceSettings.DurationMinutes
            }), token);
        }
        else
        {
            await SaveContentAsync(target, merged, inbound ? usb : otherUsb, token);
        }
        return (inbound ? merged : content, $"{merged.Entries.Count - destination.Entries.Count} credencial(is) copiada(s).");
    }

    private static bool SameSource(ExternalVaultSource left, ExternalVaultSource right) =>
        left.Kind == right.Kind && left.HostedId == right.HostedId && left.Usb == right.Usb && left.Offer?.Id == right.Offer?.Id;

    internal Task CopyAsync(string value) => _dialogs.CopySensitiveAsync(value);

    public void ShowPasskeys()
    {
        if (_operating || _session is null)
        {
            return;
        }
        var window = new PasskeyOptionsWindow();
        _dialogs.ShowVaultDialog(window);
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (_session is null || _operating)
        {
            return;
        }
        _operating = true;
        var token = _session.Token;
        try
        {
            await operation(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (!token.IsCancellationRequested && IsExpected(error))
        {
            _dialogs.ShowMessage(Describe(error), "Cofres externos", true);
        }
        finally
        {
            _operating = false;
        }
    }

    internal static bool IsExpected(Exception error) => error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or
        InvalidOperationException or FormatException or SocketException or AuthenticationException or CryptographicException or
        OperationCanceledException or JsonException or System.Runtime.InteropServices.ExternalException;

    internal static string Describe(Exception error) => error switch
    {
        OperationCanceledException => "O cofre não respondeu a tempo.",
        AuthenticationException => "A identidade do anfitrião mudou ou o canal seguro não pôde ser aberto.",
        SocketException => "O cofre não está acessível pela rede Wi-Fi.",
        JsonException => "O arquivo ou convite do cofre está inválido.",
        _ => error.Message
    };
}

public enum ExternalVaultKind { Personal, Hosted, Usb, Remote }

public sealed record ExternalVaultSource(ExternalVaultKind Kind, string Label, string Detail,
    Guid HostedId = default, UsbVolume? Usb = null, LanVaultOffer? Offer = null)
{
    public bool IsUsb => Kind == ExternalVaultKind.Usb;
}
