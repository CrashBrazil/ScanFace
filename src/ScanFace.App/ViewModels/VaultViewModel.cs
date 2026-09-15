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
    private readonly List<VaultFolder> _allFolders = [];
    private VaultEntry? _selectedEntry;
    private FolderFilterOption? _selectedFolderFilter;
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
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync, () => !IsBusy);
        RenameFolderCommand = new AsyncRelayCommand(RenameFolderAsync, CanManageSelectedFolder);
        DeleteFolderCommand = new AsyncRelayCommand(DeleteFolderAsync, CanManageSelectedFolder);
        CopyUsernameCommand = new AsyncRelayCommand(() => CopyAsync(SelectedEntry?.Username, "Usuário"), () => SelectedEntry is not null);
        CopyPasswordCommand = new AsyncRelayCommand(() => CopyAsync(SelectedEntry?.Password, "Senha"), () => SelectedEntry is not null);
        BackupCommand = new AsyncRelayCommand(BackupAsync, () => !IsBusy);
        SyncCommand = new AsyncRelayCommand(SynchronizeAsync, () => !IsBusy);
        SettingsCommand = new AsyncRelayCommand(SettingsAsync, () => !IsBusy);
        LockCommand = new AsyncRelayCommand(LockAsync);
    }

    public ObservableCollection<VaultEntry> Entries { get; } = [];
    public ObservableCollection<FolderFilterOption> FolderFilters { get; } = [];
    public DateTimeOffset LastActivityUtc { get; private set; }
    public int EntryCount => Entries.Count;
    public int TotalEntryCount => _allEntries.Count;
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
                OnPropertyChanged(nameof(SelectedEntryFolderName));
                Touch();
            }
        }
    }

    public FolderFilterOption? SelectedFolderFilter
    {
        get => _selectedFolderFilter;
        set
        {
            if (SetProperty(ref _selectedFolderFilter, value))
            {
                RenameFolderCommand.NotifyCanExecuteChanged();
                DeleteFolderCommand.NotifyCanExecuteChanged();
                ApplyFilter();
                Touch();
            }
        }
    }

    public string SelectedEntryFolderName
    {
        get
        {
            if (SelectedEntry?.FolderId is not Guid folderId)
            {
                return "Sem pasta";
            }
            return _allFolders.FirstOrDefault(folder => folder.Id == folderId)?.Name ?? "Sem pasta";
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
                AddFolderCommand.NotifyCanExecuteChanged();
                RenameFolderCommand.NotifyCanExecuteChanged();
                DeleteFolderCommand.NotifyCanExecuteChanged();
                BackupCommand.NotifyCanExecuteChanged();
                SyncCommand.NotifyCanExecuteChanged();
                SettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public AsyncRelayCommand RenameFolderCommand { get; }
    public AsyncRelayCommand DeleteFolderCommand { get; }
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
        _allFolders.Clear();
        _allFolders.AddRange(await _vault.GetFoldersAsync());
        _allEntries.Clear();
        _allEntries.AddRange(await _vault.GetEntriesAsync());
        RebuildFolderFilters();
        ApplyFilter();
        OnPropertyChanged(nameof(TotalEntryCount));
        OnPropertyChanged(nameof(SelectedEntryFolderName));
        Touch();
    }

    private void RebuildFolderFilters()
    {
        var previousKind = SelectedFolderFilter?.Kind ?? FolderFilterKind.All;
        var previousFolderId = SelectedFolderFilter?.FolderId;

        FolderFilters.Clear();
        FolderFilters.Add(new FolderFilterOption(FolderFilterKind.All, null, "Todas", _allEntries.Count));
        FolderFilters.Add(new FolderFilterOption(
            FolderFilterKind.Unfiled,
            null,
            "Sem pasta",
            _allEntries.Count(entry => entry.FolderId is null)));
        foreach (var folder in _allFolders.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            FolderFilters.Add(new FolderFilterOption(
                FolderFilterKind.Folder,
                folder.Id,
                folder.Name,
                _allEntries.Count(entry => entry.FolderId == folder.Id)));
        }

        var replacement = FolderFilters.FirstOrDefault(option =>
            option.Kind == previousKind && option.FolderId == previousFolderId) ?? FolderFilters[0];
        if (!ReferenceEquals(_selectedFolderFilter, replacement))
        {
            _selectedFolderFilter = replacement;
            OnPropertyChanged(nameof(SelectedFolderFilter));
            RenameFolderCommand.NotifyCanExecuteChanged();
            DeleteFolderCommand.NotifyCanExecuteChanged();
        }
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedEntry?.Id;
        IEnumerable<VaultEntry> filtered = _allEntries;
        filtered = SelectedFolderFilter?.Kind switch
        {
            FolderFilterKind.Unfiled => filtered.Where(entry => entry.FolderId is null),
            FolderFilterKind.Folder => filtered.Where(entry => entry.FolderId == SelectedFolderFilter.FolderId),
            _ => filtered
        };

        var query = SearchText.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(entry =>
                entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Username.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Website.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

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
        var defaultFolderId = SelectedFolderFilter?.Kind == FolderFilterKind.Folder
            ? SelectedFolderFilter.FolderId
            : null;
        var entry = _dialogs.EditEntry(null, _generator, _allFolders, defaultFolderId);
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
        var entry = _dialogs.EditEntry(SelectedEntry.Copy(), _generator, _allFolders);
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

    private async Task AddFolderAsync()
    {
        var folder = _dialogs.EditFolder(null);
        if (folder is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _vault.SaveFolderAsync(folder);
            await RefreshAsync();
            SelectedFolderFilter = FolderFilters.First(option => option.FolderId == folder.Id);
            StatusMessage = $"Pasta ‘{folder.Name}’ criada.";
        }
        catch (Exception exception)
        {
            _dialogs.ShowMessage(exception.Message, "Não foi possível criar a pasta", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RenameFolderAsync()
    {
        var current = GetSelectedFolder();
        if (current is null)
        {
            return;
        }
        var updated = _dialogs.EditFolder(current.Copy());
        if (updated is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _vault.SaveFolderAsync(updated);
            await RefreshAsync();
            StatusMessage = $"Pasta renomeada para ‘{updated.Name}’.";
        }
        catch (Exception exception)
        {
            _dialogs.ShowMessage(exception.Message, "Não foi possível renomear a pasta", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteFolderAsync()
    {
        var folder = GetSelectedFolder();
        if (folder is null || !_dialogs.Confirm(
                $"Excluir a pasta ‘{folder.Name}’? As credenciais dela serão movidas para ‘Sem pasta’.",
                "Excluir pasta"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _vault.DeleteFolderAsync(folder.Id);
            await RefreshAsync();
            StatusMessage = $"Pasta ‘{folder.Name}’ excluída. As credenciais foram preservadas.";
        }
        catch (Exception exception)
        {
            _dialogs.ShowMessage(exception.Message, "Não foi possível excluir a pasta", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private VaultFolder? GetSelectedFolder() => SelectedFolderFilter?.Kind == FolderFilterKind.Folder
        ? _allFolders.FirstOrDefault(folder => folder.Id == SelectedFolderFilter.FolderId)
        : null;

    private bool CanManageSelectedFolder() =>
        !IsBusy && SelectedFolderFilter?.Kind == FolderFilterKind.Folder;

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
        _allFolders.Clear();
        Entries.Clear();
        FolderFilters.Clear();
        await _onLocked();
    }
}

public enum FolderFilterKind
{
    All,
    Unfiled,
    Folder
}

public sealed record FolderFilterOption(FolderFilterKind Kind, Guid? FolderId, string Name, int Count)
{
    public string DisplayName => $"{Name} ({Count})";
}
