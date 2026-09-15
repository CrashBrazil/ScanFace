using ScanFace.Application;
using ScanFace.App.Presentation;
using ScanFace.App.Services;

namespace ScanFace.App.ViewModels;

public sealed class SetupViewModel : ObservableObject
{
    private readonly VaultApplicationService _vault;
    private readonly IAppDialogService _dialogs;
    private readonly Func<Task> _onCompleted;
    private string _masterPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _enableWindowsHello = true;
    private bool _isBusy;

    public SetupViewModel(VaultApplicationService vault, IAppDialogService dialogs, Func<Task> onCompleted)
    {
        _vault = vault;
        _dialogs = dialogs;
        _onCompleted = onCompleted;
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !IsBusy);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy);
    }

    public string MasterPassword { get => _masterPassword; set => SetProperty(ref _masterPassword, value); }
    public string ConfirmPassword { get => _confirmPassword; set => SetProperty(ref _confirmPassword, value); }
    public bool EnableWindowsHello { get => _enableWindowsHello; set => SetProperty(ref _enableWindowsHello, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CreateCommand.NotifyCanExecuteChanged();
                ImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }

    private async Task CreateAsync()
    {
        StatusMessage = string.Empty;
        if (MasterPassword != ConfirmPassword)
        {
            StatusMessage = "As senhas não coincidem.";
            return;
        }

        IsBusy = true;
        try
        {
            await _vault.InitializeAsync(MasterPassword, EnableWindowsHello);
            ClearPasswords();
            await _onCompleted();
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

    private async Task ImportAsync()
    {
        var path = _dialogs.ChooseBackupToOpen();
        if (path is null)
        {
            return;
        }
        var credentials = _dialogs.RequestBackupPassword();
        if (credentials is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _vault.ImportBackupAsync(path, credentials.Value.Password, credentials.Value.EnableWindowsHello);
            await _onCompleted();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Não foi possível restaurar o backup: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearPasswords()
    {
        MasterPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }
}
