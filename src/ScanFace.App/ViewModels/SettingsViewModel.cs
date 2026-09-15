using ScanFace.Application;
using ScanFace.App.Presentation;
using ScanFace.Domain;

namespace ScanFace.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly VaultApplicationService _vault;
    private readonly SyncCoordinator _sync;
    private string _serverUrl = string.Empty;
    private string _apiToken = string.Empty;
    private string _newMasterPassword = string.Empty;
    private string _confirmMasterPassword = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _windowsHelloEnabled;
    private bool _originalHelloEnabled;
    private bool _isBusy;

    public SettingsViewModel(VaultApplicationService vault, SyncCoordinator sync)
    {
        _vault = vault;
        _sync = sync;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        ChangeMasterPasswordCommand = new AsyncRelayCommand(ChangeMasterPasswordAsync, () => !IsBusy);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    public event Action? RequestClose;
    public string ServerUrl { get => _serverUrl; set => SetProperty(ref _serverUrl, value); }
    public string ApiToken { get => _apiToken; set => SetProperty(ref _apiToken, value); }
    public string NewMasterPassword { get => _newMasterPassword; set => SetProperty(ref _newMasterPassword, value); }
    public string ConfirmMasterPassword { get => _confirmMasterPassword; set => SetProperty(ref _confirmMasterPassword, value); }
    public bool WindowsHelloEnabled { get => _windowsHelloEnabled; set => SetProperty(ref _windowsHelloEnabled, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
                ChangeMasterPasswordCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ChangeMasterPasswordCommand { get; }
    public RelayCommand CloseCommand { get; }

    public async Task InitializeAsync()
    {
        var settings = await _sync.GetSettingsAsync();
        ServerUrl = settings.ServerUrl;
        ApiToken = settings.ApiToken;
        WindowsHelloEnabled = await _vault.IsWindowsHelloEnabledAsync();
        _originalHelloEnabled = WindowsHelloEnabled;
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var previous = await _sync.GetSettingsAsync();
            await _sync.SaveSettingsAsync(new SyncSettings
            {
                ServerUrl = ServerUrl.Trim(),
                ApiToken = ApiToken,
                LastRemoteVersion = previous.ServerUrl == ServerUrl.Trim() ? previous.LastRemoteVersion : null,
                LastSyncUtc = previous.ServerUrl == ServerUrl.Trim() ? previous.LastSyncUtc : null
            });

            if (WindowsHelloEnabled != _originalHelloEnabled)
            {
                if (WindowsHelloEnabled)
                {
                    await _vault.EnableWindowsHelloAsync();
                }
                else
                {
                    await _vault.DisableWindowsHelloAsync();
                }
                _originalHelloEnabled = WindowsHelloEnabled;
            }

            StatusMessage = "Configurações salvas.";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            WindowsHelloEnabled = _originalHelloEnabled;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ChangeMasterPasswordAsync()
    {
        if (NewMasterPassword != ConfirmMasterPassword)
        {
            StatusMessage = "As novas senhas não coincidem.";
            return;
        }

        IsBusy = true;
        try
        {
            await _vault.ChangeMasterPasswordAsync(NewMasterPassword);
            NewMasterPassword = string.Empty;
            ConfirmMasterPassword = string.Empty;
            StatusMessage = "Senha mestra alterada. Exporte um novo backup.";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
