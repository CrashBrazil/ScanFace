using ScanFace.Application;
using ScanFace.App.Presentation;

namespace ScanFace.App.ViewModels;

public enum UnlockStage { Ready, VerifyingHello, VerifyingPassword, Confirmed, Attention }

public sealed class UnlockViewModel : ObservableObject
{
    private readonly VaultApplicationService _vault;
    private readonly Func<Task> _onUnlocked;
    private CancellationTokenSource? _attempt;
    private string _statusMessage = string.Empty;
    private string _helloHint = string.Empty;
    private bool _helloEnabled;
    private bool _showPassword = true;
    private bool _hasPasswordInput;
    private bool _isBusy;
    private UnlockStage _stage;

    public UnlockViewModel(VaultApplicationService vault, Func<Task> onUnlocked)
    {
        _vault = vault;
        _onUnlocked = onUnlocked;
        UnlockWithHelloCommand = new AsyncRelayCommand(UnlockWithHelloAsync, () => HelloEnabled && !ShowPassword && !IsBusy);
        UsePasswordCommand = new RelayCommand(() => SelectMethod(password: true), () => !IsBusy);
        UseHelloCommand = new RelayCommand(() => SelectMethod(password: false), () => HelloEnabled && !IsBusy);
        CancelHelloCommand = new RelayCommand(() => _attempt?.Cancel(), () => Stage == UnlockStage.VerifyingHello);
    }

    public event Action? ClearPasswordRequested;

    public bool HasPasswordInput
    {
        get => _hasPasswordInput;
        set
        {
            if (SetProperty(ref _hasPasswordInput, value))
            {
                OnPropertyChanged(nameof(CanSubmitPassword));
            }
        }
    }

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string HelloHint { get => _helloHint; private set => SetProperty(ref _helloHint, value); }

    public bool HelloEnabled
    {
        get => _helloEnabled;
        private set
        {
            if (SetProperty(ref _helloEnabled, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool ShowPassword
    {
        get => _showPassword;
        private set
        {
            if (SetProperty(ref _showPassword, value))
            {
                NotifyCommands();
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
                NotifyCommands();
            }
        }
    }

    public UnlockStage Stage
    {
        get => _stage;
        private set
        {
            if (SetProperty(ref _stage, value))
            {
                CancelHelloCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(SecurityStatus));
            }
        }
    }

    public string SecurityStatus => Stage switch
    {
        UnlockStage.VerifyingHello => "Aguardando o Windows Hello",
        UnlockStage.VerifyingPassword => "Validando a senha mestra",
        UnlockStage.Confirmed => "Identidade confirmada",
        _ => "Cofre bloqueado"
    };

    public AsyncRelayCommand UnlockWithHelloCommand { get; }
    public RelayCommand UsePasswordCommand { get; }
    public RelayCommand UseHelloCommand { get; }
    public RelayCommand CancelHelloCommand { get; }
    public bool CanSubmitPassword => ShowPassword && HasPasswordInput && !IsBusy;

    public async Task InitializeAsync()
    {
        var status = await _vault.GetWindowsHelloStatusAsync();
        ApplyAvailability(status);
        if (HelloEnabled && !IsBusy && !HasPasswordInput)
        {
            ShowPassword = false;
        }
    }

    public void CancelForSystemLock(string message)
    {
        _attempt?.Cancel();
        ClearPasswordRequested?.Invoke();
        Stage = UnlockStage.Ready;
        StatusMessage = message;
    }

    private void SelectMethod(bool password)
    {
        if (!password)
        {
            ClearPasswordRequested?.Invoke();
        }
        ShowPassword = password;
        Stage = UnlockStage.Ready;
        StatusMessage = string.Empty;
    }

    private Task UnlockWithHelloAsync() => RunAttemptAsync(UnlockStage.VerifyingHello, async token =>
    {
        var result = await _vault.UnlockWithWindowsHelloAsync(token);
        if (result == WindowsHelloStatus.Verified)
        {
            return true;
        }

        StatusMessage = DescribeHello(result);
        Stage = result == WindowsHelloStatus.Canceled ? UnlockStage.Ready : UnlockStage.Attention;
        if (result is WindowsHelloStatus.NotEnabledForVault or WindowsHelloStatus.NotConfigured or
            WindowsHelloStatus.DeviceNotPresent or WindowsHelloStatus.DisabledByPolicy)
        {
            ApplyAvailability(result);
            ShowPassword = true;
        }
        else if (result is WindowsHelloStatus.RetriesExhausted or WindowsHelloStatus.KeyUnavailable)
        {
            ShowPassword = true;
        }
        return false;
    });

    public Task UnlockWithPasswordAsync(string password)
    {
        if (IsBusy || !ShowPassword || password.Length == 0)
        {
            return Task.CompletedTask;
        }
        return RunAttemptAsync(UnlockStage.VerifyingPassword, async token =>
        {
            ClearPasswordRequested?.Invoke();
            if (await _vault.UnlockWithMasterPasswordAsync(password, token))
            {
                return true;
            }
            StatusMessage = "Senha mestra incorreta. Tente novamente.";
            Stage = UnlockStage.Attention;
            return false;
        });
    }

    private async Task RunAttemptAsync(UnlockStage progress, Func<CancellationToken, Task<bool>> verify)
    {
        using var attempt = new CancellationTokenSource();
        _attempt = attempt;
        IsBusy = true;
        StatusMessage = string.Empty;
        Stage = progress;
        try
        {
            if (!await verify(attempt.Token))
            {
                return;
            }

            attempt.Token.ThrowIfCancellationRequested();
            Stage = UnlockStage.Confirmed;
            await Task.Delay(TimeSpan.FromMilliseconds(360), attempt.Token);
            await _onUnlocked();
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            _vault.Lock();
            if (Stage != UnlockStage.Ready)
            {
                Stage = UnlockStage.Ready;
                StatusMessage = "Verificação cancelada. Tente novamente ou use a senha mestra.";
            }
        }
        catch (Exception)
        {
            _vault.Lock();
            Stage = UnlockStage.Attention;
            StatusMessage = "Não foi possível abrir o cofre. Tente novamente.";
        }
        finally
        {
            _attempt = null;
            IsBusy = false;
        }
    }

    private void ApplyAvailability(WindowsHelloStatus status)
    {
        HelloEnabled = status is WindowsHelloStatus.Available or WindowsHelloStatus.DeviceBusy;
        HelloHint = HelloEnabled ? string.Empty : DescribeHello(status);
    }

    private static string DescribeHello(WindowsHelloStatus status) => status switch
    {
        WindowsHelloStatus.NotEnabledForVault => "Para usar o Windows Hello, entre e ative a opção nas Configurações do ScanFace.",
        WindowsHelloStatus.NotConfigured => "Configure um método de entrada nas opções do Windows Hello.",
        WindowsHelloStatus.DeviceNotPresent => "O Windows não encontrou um dispositivo de verificação disponível.",
        WindowsHelloStatus.DisabledByPolicy => "A política deste computador desativou o Windows Hello.",
        WindowsHelloStatus.DeviceBusy => "O dispositivo de verificação está ocupado. Tente novamente ou use a senha mestra.",
        WindowsHelloStatus.Canceled => "Verificação cancelada. Tente novamente ou use a senha mestra.",
        WindowsHelloStatus.RetriesExhausted => "O Windows encerrou as tentativas. Use a senha mestra para continuar.",
        WindowsHelloStatus.KeyUnavailable => "Entre com a senha mestra e configure novamente o Windows Hello no ScanFace.",
        _ => "O Windows Hello está indisponível. Use a senha mestra para continuar."
    };

    private void NotifyCommands()
    {
        UnlockWithHelloCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSubmitPassword));
        UsePasswordCommand.NotifyCanExecuteChanged();
        UseHelloCommand.NotifyCanExecuteChanged();
    }
}
