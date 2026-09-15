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
    private object? _currentViewModel;

    public ShellViewModel(
        VaultApplicationService vault,
        SyncCoordinator sync,
        PasswordGeneratorService generator,
        IAppDialogService dialogs)
    {
        _vault = vault;
        _sync = sync;
        _generator = generator;
        _dialogs = dialogs;
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
        if (CurrentViewModel is VaultViewModel &&
            _vault.IsUnlocked &&
            DateTimeOffset.UtcNow - (CurrentViewModel as VaultViewModel)!.LastActivityUtc >= AutoLockTimeout)
        {
            _vault.Lock();
            _ = ShowUnlockAsync("Cofre bloqueado após 5 minutos de inatividade.");
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
        var viewModel = new VaultViewModel(_vault, _sync, _generator, _dialogs, () => ShowUnlockAsync());
        CurrentViewModel = viewModel;
        await viewModel.InitializeAsync();
    }
}
