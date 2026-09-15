using System.Collections.ObjectModel;
using ScanFace.Application;
using ScanFace.App.Presentation;
using ScanFace.App.Services;
using ScanFace.Domain;

namespace ScanFace.App.ViewModels;

public sealed class VaultViewModel : ObservableObject
{
    private readonly VaultApplicationService _vault;
    private readonly SyncCoordinator _sync;
    private readonly PasswordGeneratorService _generator;
    private readonly IAppDialogService _dialogs;
    private readonly Func<Task> _onLocked;
    private readonly List<VaultEntry> _allEntries = [];
    private VaultEntry? _selectedEntry;
    private string _searchText = string.Empty;
    private string _statusMessage = "Cofre desbloqueado";
    private bool _isBusy;

    public VaultViewModel(
        VaultApplicationService vault,
        SyncCoordinator sync,
        PasswordGeneratorService generator,
        IAppDialogService dialogs,
        Func<Task> onLocked)
    {
        _vault = vault;
        _sync = sync;
        _generator = generator;
        _dialogs = dialogs;
        _onLocked = onLocked;
        LastActivityUtc = DateTimeOffset.UtcNow;

        AddCommand = new AsyncRelayCommand(AddAsync, () => !IsBusy);
        EditCommand = new AsyncRelayCommand(EditAsync, () => SelectedEntry is not null && !IsBusy);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedEntry is not null && !IsBusy);
        CopyUsernameCommand = new AsyncRelayCommand(() => CopyAsync(SelectedEntry?.Username, "Usuário"), () => SelectedEntry is not null);
        CopyPasswordCommand = new AsyncRelayCommand(() => CopyAsync(SelectedEntry?.Password, "Senha"), () => SelectedEntry is not null);
        BackupCommand = new AsyncRelayCommand(BackupAsync, () => !IsBusy);
        SyncCommand = new AsyncRelayCommand(SynchronizeAsync, () => !IsBusy);
        SettingsCommand = new AsyncRelayCommand(SettingsAsync, () => !IsBusy);
        LockCommand = new AsyncRelayCommand(LockAsync);
    }

    public ObservableCollection<VaultEntry> Entries { get; } = [];
    public DateTimeOffset LastActivityUtc { get; private set; }
    public int EntryCount => Entries.Count;
    public bool IsEmpty => Entries.Count == 0;

    public VaultEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                EditCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
                CopyUsernameCommand.NotifyCanExecuteChanged();
                CopyPasswordCommand.NotifyCanExecuteChanged();
                Touch();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
                Touch();
            }
        }
    }

    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                AddCommand.NotifyCanExecuteChanged();
                EditCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
                BackupCommand.NotifyCanExecuteChanged();
                SyncCommand.NotifyCanExecuteChanged();
                SettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand CopyUsernameCommand { get; }
    public AsyncRelayCommand CopyPasswordCommand { get; }
    public AsyncRelayCommand BackupCommand { get; }
    public AsyncRelayCommand SyncCommand { get; }
    public AsyncRelayCommand SettingsCommand { get; }
    public AsyncRelayCommand LockCommand { get; }

    public async Task InitializeAsync() => await RefreshAsync();

    public void Touch()
    {
        LastActivityUtc = DateTimeOffset.UtcNow;
        _vault.Touch();
    }

    private async Task RefreshAsync()
    {
        _allEntries.Clear();
        _allEntries.AddRange(await _vault.GetEntriesAsync());
        ApplyFilter();
        Touch();
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedEntry?.Id;
        var query = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allEntries
            : _allEntries.Where(entry =>
                entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Username.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Website.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        Entries.Clear();
        foreach (var entry in filtered.OrderByDescending(item => item.IsFavorite).ThenBy(item => item.Name))
        {
            Entries.Add(entry);
        }
        SelectedEntry = selectedId.HasValue ? Entries.FirstOrDefault(item => item.Id == selectedId) : Entries.FirstOrDefault();
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task AddAsync()
    {
        var entry = _dialogs.EditEntry(null, _generator);
        if (entry is null)
        {
            return;
        }
        await SaveAndRefreshAsync(entry, "Credencial adicionada.");
    }

    private async Task EditAsync()
    {
        if (SelectedEntry is null)
        {
            return;
        }
        var entry = _dialogs.EditEntry(SelectedEntry.Copy(), _generator);
        if (entry is null)
        {
            return;
        }
        await SaveAndRefreshAsync(entry, "Credencial atualizada.");
    }

    private async Task SaveAndRefreshAsync(VaultEntry entry, string message)
    {
        IsBusy = true;
        try
        {
            await _vault.SaveEntryAsync(entry);
            await RefreshAsync();
            SelectedEntry = Entries.FirstOrDefault(item => item.Id == entry.Id);
            StatusMessage = message;
        }
        catch (Exception exception)
        {
            _dialogs.ShowMessage(exception.Message, "Não foi possível salvar", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (SelectedEntry is null || !_dialogs.Confirm($"Excluir ‘{SelectedEntry.Name}’ do cofre?", "Excluir credencial"))
        {
            return;
        }
        IsBusy = true;
        try
        {
            await _vault.DeleteEntryAsync(SelectedEntry.Id);
            await RefreshAsync();
            StatusMessage = "Credencial excluída.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CopyAsync(string? value, string label)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }
        StatusMessage = $"{label} copiado. A área de transferência será limpa em 30 segundos.";
        Touch();
        await _dialogs.CopySensitiveAsync(value);
    }

    private async Task BackupAsync()
    {
        var path = _dialogs.ChooseBackupToSave();
        if (path is null)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await _vault.ExportBackupAsync(path);
            StatusMessage = "Backup cifrado exportado.";
        }
        catch (Exception exception)
        {
            _dialogs.ShowMessage(exception.Message, "Falha no backup", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SynchronizeAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _sync.SynchronizeAsync();
            StatusMessage = result.Message;
            if (result.Status == SyncStatus.Downloaded)
            {
                await RefreshAsync();
            }
            if (result.Status is SyncStatus.Disabled or SyncStatus.Conflict)
            {
                _dialogs.ShowMessage(result.Message, "Sincronização", result.Status == SyncStatus.Conflict);
            }
        }
        catch (Exception exception)
        {
            _dialogs.ShowMessage(exception.Message, "Falha na sincronização", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SettingsAsync()
    {
        await _dialogs.ShowSettingsAsync(_vault, _sync);
        StatusMessage = "Configurações atualizadas.";
        Touch();
    }

    private async Task LockAsync()
    {
        _vault.Lock();
        _allEntries.Clear();
        Entries.Clear();
        await _onLocked();
    }
}
