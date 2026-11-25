using System.Windows;
using System.Windows.Controls;
using ScanFace.App.Services;
using ScanFace.Domain;
using ScanFace.Infrastructure;

namespace ScanFace.App.Views;

public partial class ExternalVaultWindow : Window
{
    private readonly ExternalVaultWorkspace _workspace;
    private readonly ExternalVaultSource _source;
    private readonly DialogService _dialogs;
    private readonly UsbVaultSession? _usb;
    private readonly WifiSubnet? _network;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private ExternalVaultContent _content;
    private bool _busy;
    private bool _closed;
    private bool _revealed;

    public ExternalVaultWindow(ExternalVaultWorkspace workspace, ExternalVaultSource source, ExternalVaultContent content,
        DialogService dialogs, CancellationToken token, UsbVaultSession? usb = null, WifiSubnet? network = null)
    {
        _workspace = workspace;
        _source = source;
        _content = content;
        _dialogs = dialogs;
        _usb = usb;
        _network = network;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _token = _lifetime.Token;
        InitializeComponent();
        KindIcon.Text = (string)FindResource(source.IsUsb ? "UsbGlyph" : "NetworkGlyph");
        Publishing.Visibility = source.Kind == ExternalVaultKind.Hosted ? Visibility.Visible : Visibility.Collapsed;
        EditActions.Visibility = ImportItems.Visibility = EditEntry.Visibility = DeleteEntry.Visibility =
            source.Kind == ExternalVaultKind.Remote ? Visibility.Collapsed : Visibility.Visible;
        Subtitle.Text = source.Kind switch
        {
            ExternalVaultKind.Usb => $"Cofre USB · {source.Usb?.Root}",
            ExternalVaultKind.Hosted => "Cofre de rede · anfitrião",
            _ => "Cofre de rede · somente leitura"
        };
        Refresh();
    }

    private VaultEntry? Selected => Items.SelectedItem as VaultEntry;

    private void Refresh(Guid? select = null)
    {
        Title = VaultTitle.Text = _content.Name;
        var selectedFolder = (Folders.SelectedItem as FolderFilter)?.Id;
        Folders.ItemsSource = new[] { new FolderFilter(null, "Todas as pastas") }
            .Concat(_content.Folders.OrderBy(folder => folder.Name).Select(folder => new FolderFilter(folder.Id, folder.Name)))
            .ToArray();
        Folders.SelectedItem = ((IEnumerable<FolderFilter>)Folders.ItemsSource).FirstOrDefault(folder => folder.Id == selectedFolder);
        RefreshEntries(select);
        RefreshSharing();
    }

    internal void RefreshSharing()
    {
        if (_closed || _source.Kind != ExternalVaultKind.Hosted)
        {
            return;
        }
        var hosted = _workspace.Hosted(_source.HostedId);
        var offer = _workspace.ActiveOffer(_source.HostedId);
        var access = hosted.Settings.Access switch
        {
            LanAccess.Password => "rede e senha",
            LanAccess.LoginAndPassword => "login e senha",
            _ => "rede sem senha"
        };
        ShareState.Text = offer is null ? $"Compartilhamento desligado · {access}" : $"Compartilhando · {access}";
        ShareCode.Text = offer is null ? string.Empty : $"Verificação · {offer.VerificationCode}";
        CopyInvitation.Visibility = offer is null ? Visibility.Collapsed : Visibility.Visible;
        ShareButton.Content = offer is null ? "Compartilhar" : "Parar";
    }

    private void RefreshEntries(Guid? select = null)
    {
        if (_closed)
        {
            return;
        }
        var selected = select ?? Selected?.Id;
        var folderId = (Folders.SelectedItem as FolderFilter)?.Id;
        var visible = _content.Entries.Where(entry => folderId is null || entry.FolderId == folderId)
            .OrderByDescending(entry => entry.IsFavorite).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Items.ItemsSource = visible;
        Items.SelectedItem = visible.FirstOrDefault(item => item.Id == selected) ?? visible.FirstOrDefault();
    }

    private void FolderChanged(object sender, SelectionChangedEventArgs e) => RefreshEntries();

    private void EntryChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = Selected;
        Details.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        NoSelection.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        EntryName.Text = item?.Name ?? string.Empty;
        EntryUsername.Text = item?.Username ?? string.Empty;
        EntrySite.Text = item?.Website ?? string.Empty;
        EntryNotes.Text = item?.Notes ?? string.Empty;
        HidePassword();
    }

    private void HidePassword()
    {
        _revealed = false;
        EntryPassword.Text = "••••••••••••";
        RevealPassword.Content = "Mostrar senha";
    }

    private void OnDeactivated(object? sender, EventArgs e) => HidePassword();

    private void RevealClick(object sender, RoutedEventArgs e)
    {
        if (_usb is not null && !_usb.IsPresent)
        {
            Close();
            return;
        }
        if (_revealed) { HidePassword(); return; }
        if (Selected is { } selected)
        {
            _revealed = true;
            EntryPassword.Text = selected.Password;
            RevealPassword.Content = "Ocultar senha";
        }
    }

    private async void AddEntryClick(object sender, RoutedEventArgs e) => await RunAsync(() => EditAsync(null));
    private async void EditEntryClick(object sender, RoutedEventArgs e) => await RunAsync(() => EditAsync(Selected));
    private async void AddFolderClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var folder = _dialogs.EditFolder(null);
        if (folder is null)
        {
            return;
        }
        if (_content.Folders.Any(item => string.Equals(item.Name, folder.Name, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new InvalidOperationException("Já existe uma pasta com esse nome.");
        }
        await SaveAsync(_content with { Folders = _content.Folders.Append(folder).ToArray() });
    });

    private Task EditAsync(VaultEntry? existing)
    {
        var editor = _dialogs.EditEntry(existing?.Copy(), new ScanFace.Application.PasswordGeneratorService(),
            _content.Folders, (Folders.SelectedItem as FolderFilter)?.Id);
        return editor is null ? Task.CompletedTask :
            SaveAsync(_content with { Entries = _content.Entries.Where(item => item.Id != editor.Id).Append(editor).ToArray() }, editor.Id);
    }

    private async void DeleteEntryClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (Selected is { } selected && _dialogs.Confirm($"Excluir «{selected.Name}» deste cofre?", "Excluir credencial"))
        {
            await SaveAsync(_content with { Entries = _content.Entries.Where(item => item.Id != selected.Id).ToArray() });
        }
    });

    private async Task SaveAsync(ExternalVaultContent updated, Guid? select = null)
    {
        _token.ThrowIfCancellationRequested();
        await _workspace.SaveContentAsync(_source, updated, _usb, _token);
        _token.ThrowIfCancellationRequested();
        _content = updated;
        Refresh(select);
    }

    private async void ImportClick(object sender, RoutedEventArgs e) => await RunAsync(() => TransferAsync(inbound: true));
    private async void ExportClick(object sender, RoutedEventArgs e) => await RunAsync(() => TransferAsync(inbound: false));

    private async Task TransferAsync(bool inbound)
    {
        var result = await _workspace.TransferAsync(_source, _content, _usb, inbound, _token);
        _token.ThrowIfCancellationRequested();
        _content = result.Content;
        Status.Text = result.Message;
        Refresh();
    }

    private async void ConfigureClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        _content = await _workspace.EditHostedSettingsAsync(_source.HostedId, _token);
        _token.ThrowIfCancellationRequested();
        Refresh();
    });

    private async void ShareClick(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        _workspace.ToggleHost(_source.HostedId);
        RefreshSharing();
        return Task.CompletedTask;
    });

    private async void CopyInvitationClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_workspace.ActiveOffer(_source.HostedId) is { } offer)
        {
            await _workspace.CopyAsync(offer.ToInvitation());
            Status.Text = "Convite copiado por até 30 segundos.";
        }
    });

    private async void CopyUserClick(object sender, RoutedEventArgs e) => await RunAsync(() => CopyFieldAsync(Selected?.Username, "Usuário copiado."));
    private async void CopyPasswordClick(object sender, RoutedEventArgs e) => await RunAsync(() => CopyFieldAsync(Selected?.Password, "Senha copiada."));

    private async Task CopyFieldAsync(string? value, string status)
    {
        if (!string.IsNullOrEmpty(value))
        {
            await _workspace.CopyAsync(value);
            Status.Text = status;
        }
    }

    internal void CheckAvailability(IReadOnlyList<UsbVolume> devices, WifiSubnet? wifi)
    {
        if ((_usb is not null && !devices.Any(device => device.Root == _usb.Device.Root && device.DeviceId == _usb.Device.DeviceId)) ||
            (_source.Kind == ExternalVaultKind.Remote && wifi != _network))
        {
            Close();
            return;
        }
        RefreshSharing();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _token.IsCancellationRequested)
        {
            return;
        }
        if (_usb is not null && !_usb.IsPresent)
        {
            Close();
            return;
        }
        _busy = true;
        EditActions.IsEnabled = Publishing.IsEnabled = TransferActions.IsEnabled = false;
        Status.Text = string.Empty;
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (Exception error) when (ExternalVaultWorkspace.IsExpected(error))
        {
            if (!_closed)
            {
                Status.Text = ExternalVaultWorkspace.Describe(error);
            }
        }
        finally
        {
            _busy = false;
            EditActions.IsEnabled = Publishing.IsEnabled = TransferActions.IsEnabled = true;
        }
    }

    internal void ClearSensitiveView()
    {
        _content = _content with { Folders = [], Entries = [] };
        Items.ItemsSource = null;
        Folders.ItemsSource = null;
        EntryName.Text = EntryUsername.Text = EntrySite.Text = EntryNotes.Text = string.Empty;
        HidePassword();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        ClearSensitiveView();
    }

    private sealed record FolderFilter(Guid? Id, string Name);
}
