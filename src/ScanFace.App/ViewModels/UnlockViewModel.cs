using ScanFace.Application;
using ScanFace.App.Presentation;

namespace ScanFace.App.ViewModels;

public sealed class UnlockViewModel : ObservableObject
{
    private readonly VaultApplicationService _vault;
    private readonly Func<Task> _onUnlocked;
    private string _masterPassword = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _helloEnabled;
    private bool _isBusy;

    public UnlockViewModel(VaultApplicationService vault, Func<Task> onUnlocked)
    {
        _vault = vault;
        _onUnlocked = onUnlocked;
        UnlockWithHelloCommand = new AsyncRelayCommand(UnlockWithHelloAsync, () => HelloEnabled && !IsBusy);
        UnlockWithPasswordCommand = new AsyncRelayCommand(UnlockWithPasswordAsync, () => !IsBusy);
    }

    public string MasterPassword { get => _masterPassword; set => SetProperty(ref _masterPassword, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public bool HelloEnabled
    {
        get => _helloEnabled;
        private set
        {
            if (SetProperty(ref _helloEnabled, value))
            {
                UnlockWithHelloCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                UnlockWithHelloCommand.NotifyCanExecuteChanged();
                UnlockWithPasswordCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand UnlockWithHelloCommand { get; }
    public AsyncRelayCommand UnlockWithPasswordCommand { get; }

    public async Task InitializeAsync() =>
        HelloEnabled = await _vault.IsWindowsHelloEnabledAsync() && await _vault.IsWindowsHelloAvailableAsync();

    private async Task UnlockWithHelloAsync()
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            if (await _vault.UnlockWithWindowsHelloAsync())
            {
                await _onUnlocked();
            }
            else
            {
                StatusMessage = "O Windows Hello não confirmou o acesso.";
            }
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

    private async Task UnlockWithPasswordAsync()
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            if (await _vault.UnlockWithMasterPasswordAsync(MasterPassword))
            {
                MasterPassword = string.Empty;
                await _onUnlocked();
            }
            else
            {
                StatusMessage = "Senha mestra incorreta.";
            }
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
