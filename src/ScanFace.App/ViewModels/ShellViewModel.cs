using ScanFace.Application;
using ScanFace.App.Presentation;
using ScanFace.App.Services;

namespace ScanFace.App.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private static readonly TimeSpan AutoLockTimeout = TimeSpan.FromMinutes(5);
    private readonly VaultApplicationService _vault;
    private readonly SyncCoordinator _sync;
    private readonly PasswordGeneratorService _generator;
    private readonly IAppDialogService _dialogs;
    private readonly ExternalVaultWorkspace? _externalVaults;
    private object? _currentViewModel;
    private string? _systemLockDuringSetup;

    public ShellViewModel(
        VaultApplicationService vault,
        SyncCoordinator sync,
        PasswordGeneratorService generator,
        IAppDialogService dialogs,
        ExternalVaultWorkspace? externalVaults = null)
    {
        _vault = vault;
        _sync = sync;
        _generator = generator;
        _dialogs = dialogs;
        _externalVaults = externalVaults;
    }

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public async Task InitializeAsync()
    {
        if (await _vault.VaultExistsAsync())
        {
            await ShowUnlockAsync();
        }
        else
        {
            await ShowSetupAsync();
        }
    }

    public void Touch()
    {
        if (CurrentViewModel is VaultViewModel vaultViewModel)
        {
            vaultViewModel.Touch();
        }
    }

    public void CheckAutoLock()
    {
        if (CurrentViewModel is VaultViewModel vaultViewModel &&
            _vault.IsUnlocked &&
            DateTimeOffset.UtcNow - vaultViewModel.LastActivityUtc >= AutoLockTimeout)
        {
            _ = vaultViewModel.LockAsync("Cofre bloqueado após 5 minutos de inatividade.");
        }
    }

    public void LockFromSystem(string message)
    {
        if (CurrentViewModel is VaultViewModel vaultViewModel)
        {
            _ = vaultViewModel.LockAsync(message);
        }
        else if (CurrentViewModel is UnlockViewModel unlockViewModel)
        {
            unlockViewModel.CancelForSystemLock(message);
            _vault.Lock();
        }
        else if (CurrentViewModel is SetupViewModel)
        {
            _systemLockDuringSetup = message;
        }
    }

    private Task ShowSetupAsync()
    {
        CurrentViewModel = new SetupViewModel(_vault, _dialogs, ShowVaultAsync);
        return Task.CompletedTask;
    }

    private async Task ShowUnlockAsync(string? message = null)
    {
        var viewModel = new UnlockViewModel(_vault, ShowVaultAsync) { StatusMessage = message ?? string.Empty };
        CurrentViewModel = viewModel;
        await viewModel.InitializeAsync();
    }

    private async Task ShowVaultAsync()
    {
        if (!_vault.IsUnlocked)
        {
            return;
        }
        var previousView = CurrentViewModel;
        if (previousView is SetupViewModel && _systemLockDuringSetup is { } message)
        {
            _systemLockDuringSetup = null;
            _vault.Lock();
            await ShowUnlockAsync(message);
            return;
        }
        var viewModel = new VaultViewModel(_vault, _sync, _generator, _dialogs, ShowUnlockAsync, _externalVaults);
        await viewModel.InitializeAsync();
        if (previousView is SetupViewModel && _systemLockDuringSetup is { } duringInitialization)
        {
            _systemLockDuringSetup = null;
            _vault.Lock();
            await ShowUnlockAsync(duringInitialization);
            return;
        }
        if (!_vault.IsUnlocked || !ReferenceEquals(CurrentViewModel, previousView))
        {
            return;
        }
        CurrentViewModel = viewModel;
        if (_externalVaults is not null)
        {
            await _externalVaults.StartAsync();
        }
    }
}
