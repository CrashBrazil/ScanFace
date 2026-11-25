using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
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
    private readonly Func<string?, Task> _onLocked;
    private IReadOnlyList<VaultEntry> _allEntries = [];
    private IReadOnlyList<VaultFolder> _allFolders = [];
    private IReadOnlyList<FolderFilterOption> _folderFilters = [];
    private VaultEntry? _selectedEntry;
    private FolderFilterOption? _selectedFolderFilter;
    private EntrySortOption _selectedSort;
    private string _searchText = string.Empty;
    private string _statusMessage = "Cofre desbloqueado";
    private DateTimeOffset _recentCutoff;
    private bool _isPasswordVisible;
    private bool _isBusy;
    private bool _isLocked;

    public VaultViewModel(
        VaultApplicationService vault,
        SyncCoordinator sync,
        PasswordGeneratorService generator,
        IAppDialogService dialogs,
        Func<string?, Task> onLocked,
        ExternalVaultWorkspace? externalVaults = null)
    {
        _vault = vault;
        _sync = sync;
        _generator = generator;
        _dialogs = dialogs;
        _onLocked = onLocked;
        ExternalVaults = externalVaults;
        _selectedSort = SortOptions[0];
        LastActivityUtc = DateTimeOffset.UtcNow;

        AddCommand = new AsyncRelayCommand(AddAsync, CanChangeVault);
        EditCommand = new AsyncRelayCommand(EditAsync, () => HasSelection && CanChangeVault());
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => HasSelection && CanChangeVault());
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync, CanChangeVault);
        RenameFolderCommand = new AsyncRelayCommand(RenameFolderAsync, CanManageSelectedFolder);
        DeleteFolderCommand = new AsyncRelayCommand(DeleteFolderAsync, CanManageSelectedFolder);
        CopyUsernameCommand = new AsyncRelayCommand(() => CopyAsync(SelectedEntry?.Username, "Usuário copiado."), () => HasUsername);
        CopyPasswordCommand = new AsyncRelayCommand(() => CopyAsync(SelectedEntry?.Password, "Senha copiada."), () => HasPassword);
        CopyWebsiteCommand = new AsyncRelayCommand(() => CopyAsync(SelectedEntry?.Website, "Endereço copiado."), () => HasWebsite);
        TogglePasswordCommand = new RelayCommand(() => IsPasswordVisible = !IsPasswordVisible, () => HasPassword);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => HasSelection && CanChangeVault());
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => HasSearch);
        GeneratorCommand = new RelayCommand(() => _dialogs.ShowGenerator(_generator), CanChangeVault);
        BackupCommand = new AsyncRelayCommand(BackupAsync, CanChangeVault);
        SyncCommand = new AsyncRelayCommand(SynchronizeAsync, CanChangeVault);
        SettingsCommand = new AsyncRelayCommand(SettingsAsync, CanChangeVault);
        LockCommand = new AsyncRelayCommand(() => LockAsync(), () => !_isLocked);
    }

    public ObservableCollection<VaultEntry> Entries { get; } = [];
    public ExternalVaultWorkspace? ExternalVaults { get; }
    public IReadOnlyList<FolderFilterOption> FolderFilters
    {
        get => _folderFilters;
        private set => SetProperty(ref _folderFilters, value);
    }

    public IReadOnlyList<EntrySortOption> SortOptions { get; } =
    [
        new(EntrySort.Default, "Padrão"),
        new(EntrySort.Name, "Nome A–Z"),
        new(EntrySort.Updated, "Atualização")
    ];

    public EntrySortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (value is not null && SetProperty(ref _selectedSort, value))
            {
                ApplyFilter();
            }
        }
    }

    public DateTimeOffset LastActivityUtc { get; private set; }
    public int EntryCount => Entries.Count;
    public int TotalEntryCount => _allEntries.Count;
    public bool IsEmpty => Entries.Count == 0;
    public bool HasSelection => SelectedEntry is not null;
    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);
    public bool HasUsername => !string.IsNullOrWhiteSpace(SelectedEntry?.Username);
    public bool HasPassword => !string.IsNullOrEmpty(SelectedEntry?.Password);
    public bool HasWebsite => !string.IsNullOrWhiteSpace(SelectedEntry?.Website);
    public bool HasNotes => !string.IsNullOrWhiteSpace(SelectedEntry?.Notes);
    public bool IsFolderSelected => SelectedFolderFilter?.Kind == FolderFilterKind.Folder;
    public string ScopeName => SelectedFolderFilter?.Name ?? "Todas as credenciais";
    public string SearchPlaceholder => SelectedFolderFilter?.Kind switch
    {
        FolderFilterKind.Favorites => "Buscar nos favoritos",
        FolderFilterKind.Recent => "Buscar nas recentes",
        FolderFilterKind.Folder => "Buscar nesta pasta",
        FolderFilterKind.Unfiled => "Buscar itens sem pasta",
        _ => "Buscar no cofre"
    };
    public string ResultsLabel => EntryCount == 1 ? "1 credencial" : $"{EntryCount} credenciais";
    public string FavoriteActionLabel => SelectedEntry?.IsFavorite == true ? "Remover dos favoritos" : "Adicionar aos favoritos";
    public string PasswordActionLabel => IsPasswordVisible ? "Ocultar senha" : "Mostrar senha";
    public string PasswordDisplay => IsPasswordVisible ? SelectedEntry?.Password ?? string.Empty : "••••••••••••";
    public string UpdatedLabel => SelectedEntry?.UpdatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm") ?? string.Empty;
    public bool ShowCreateFromEmpty => IsEmpty && !HasSearch && SelectedFolderFilter?.Kind is not (FolderFilterKind.Favorites or FolderFilterKind.Recent);

    public string EmptyTitle => HasSearch ? "Nenhum resultado" : SelectedFolderFilter?.Kind switch
    {
        FolderFilterKind.Favorites => "Nenhum favorito",
        FolderFilterKind.Recent => "Nenhuma atualização recente",
        FolderFilterKind.Folder => "Esta pasta está vazia",
        FolderFilterKind.Unfiled => "Nenhuma credencial sem pasta",
        _ => "Seu cofre está vazio"
    };

    public string EmptyDescription => HasSearch ? "Tente outro nome, usuário ou site." : SelectedFolderFilter?.Kind switch
    {
        FolderFilterKind.Favorites => "Use a estrela nos detalhes de uma credencial para encontrá-la aqui.",
        FolderFilterKind.Recent => "As credenciais atualizadas nos últimos 30 dias aparecem aqui.",
        FolderFilterKind.Folder => "Adicione uma credencial ou mova uma existente para esta pasta.",
        FolderFilterKind.Unfiled => "As credenciais sem uma pasta escolhida aparecem aqui.",
        _ => "Adicione sua primeira credencial para começar."
    };

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        private set
        {
            if (SetProperty(ref _isPasswordVisible, value))
            {
                OnPropertyChanged(nameof(PasswordDisplay));
                OnPropertyChanged(nameof(PasswordActionLabel));
            }
        }
    }

    public void HidePassword() => IsPasswordVisible = false;

    public VaultEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (ReferenceEquals(_selectedEntry, value))
            {
                return;
            }

            HidePassword();
            SetProperty(ref _selectedEntry, value);
            EditCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            CopyUsernameCommand.NotifyCanExecuteChanged();
            CopyPasswordCommand.NotifyCanExecuteChanged();
            CopyWebsiteCommand.NotifyCanExecuteChanged();
            ToggleFavoriteCommand.NotifyCanExecuteChanged();
            TogglePasswordCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasUsername));
            OnPropertyChanged(nameof(HasPassword));
            OnPropertyChanged(nameof(HasWebsite));
            OnPropertyChanged(nameof(HasNotes));
            OnPropertyChanged(nameof(FavoriteActionLabel));
            OnPropertyChanged(nameof(UpdatedLabel));
            OnPropertyChanged(nameof(SelectedEntryFolderName));
        }
    }

    public FolderFilterOption? SelectedFolderFilter
    {
        get => _selectedFolderFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedFolderFilter, value))
            {
                NotifyFolderScope();
                ApplyFilter();
            }
        }
    }

    public string SelectedEntryFolderName =>
        _allFolders.FirstOrDefault(folder => folder.Id == SelectedEntry?.FolderId)?.Name ?? "Sem pasta";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(HasSearch));
                ClearSearchCommand.NotifyCanExecuteChanged();
                ApplyFilter();
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
                NotifyVaultCommands();
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
    public AsyncRelayCommand CopyWebsiteCommand { get; }
    public AsyncRelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand TogglePasswordCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand GeneratorCommand { get; }
    public AsyncRelayCommand BackupCommand { get; }
    public AsyncRelayCommand SyncCommand { get; }
    public AsyncRelayCommand SettingsCommand { get; }
    public AsyncRelayCommand LockCommand { get; }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task RefreshAfterTransferAsync() => _isLocked || IsBusy
        ? Task.CompletedTask : RunVaultActionAsync(RefreshAsync, "Não foi possível atualizar o cofre pessoal");

    public void Touch()
    {
        if (_isLocked)
        {
            return;
        }
        LastActivityUtc = DateTimeOffset.UtcNow;
        _vault.Touch();
    }

    private async Task RefreshAsync()
    {
        var folders = await _vault.GetFoldersAsync();
        if (_isLocked)
        {
            return;
        }
        var entries = await _vault.GetEntriesAsync();
        if (_isLocked)
        {
            return;
        }
        _allFolders = folders;
        _allEntries = entries;
        _recentCutoff = DateTimeOffset.UtcNow.AddDays(-30);
        RebuildFolderFilters();
        OnPropertyChanged(nameof(TotalEntryCount));
        OnPropertyChanged(nameof(SelectedEntryFolderName));
        Touch();
    }

    private void RebuildFolderFilters()
    {
        var previous = SelectedFolderFilter;
        var folderCounts = _allEntries.ToLookup(entry => entry.FolderId);
        var filters = new List<FolderFilterOption>
        {
            new(FolderFilterKind.All, null, "Todas as credenciais", _allEntries.Count),
            new(FolderFilterKind.Favorites, null, "Favoritos", _allEntries.Count(entry => entry.IsFavorite)),
            new(FolderFilterKind.Recent, null, "Recentes", _allEntries.Count(entry => entry.UpdatedAtUtc >= _recentCutoff)),
            new(FolderFilterKind.Unfiled, null, "Sem pasta", folderCounts[null].Count())
        };
        foreach (var folder in _allFolders.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            filters.Add(new(FolderFilterKind.Folder, folder.Id, folder.Name, folderCounts[folder.Id].Count()));
        }

        if (!FolderFilters.SequenceEqual(filters))
        {
            var replacement = previous is null ? filters[0] : filters.FirstOrDefault(option =>
                option.Kind == previous.Kind && option.FolderId == previous.FolderId) ?? filters[0];
            FolderFilters = filters;
            _selectedFolderFilter = replacement;
            OnPropertyChanged(nameof(SelectedFolderFilter));
            NotifyFolderScope();
        }
        ApplyFilter();
    }

    private void NotifyFolderScope()
    {
        OnPropertyChanged(nameof(ScopeName));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(IsFolderSelected));
        RenameFolderCommand.NotifyCanExecuteChanged();
        DeleteFolderCommand.NotifyCanExecuteChanged();
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedEntry?.Id;
        IEnumerable<VaultEntry> filtered = _allEntries;
        filtered = SelectedFolderFilter?.Kind switch
        {
            FolderFilterKind.Favorites => filtered.Where(entry => entry.IsFavorite),
            FolderFilterKind.Recent => filtered.Where(entry => entry.UpdatedAtUtc >= _recentCutoff),
            FolderFilterKind.Unfiled => filtered.Where(entry => entry.FolderId is null),
            FolderFilterKind.Folder => filtered.Where(entry => entry.FolderId == SelectedFolderFilter.FolderId),
            _ => filtered
        };

        var query = SearchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (query.Length > 0)
        {
            var comparison = CultureInfo.CurrentCulture.CompareInfo;
            const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            filtered = filtered.Where(entry => query.All(term =>
                comparison.IndexOf(entry.Name, term, options) >= 0 ||
                comparison.IndexOf(entry.Username, term, options) >= 0 ||
                comparison.IndexOf(entry.Website, term, options) >= 0));
        }

        filtered = SelectedSort.Sort switch
        {
            EntrySort.Name => filtered.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            EntrySort.Updated => SortByUpdate(filtered),
            _ when SelectedFolderFilter?.Kind == FolderFilterKind.Recent => SortByUpdate(filtered),
            _ => filtered.OrderByDescending(entry => entry.IsFavorite)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        Entries.Clear();
        foreach (var entry in filtered)
        {
            Entries.Add(entry);
        }
        SelectedEntry = Entries.FirstOrDefault(item => item.Id == selectedId) ?? Entries.FirstOrDefault();
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ResultsLabel));
        OnPropertyChanged(nameof(ShowCreateFromEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    private static IEnumerable<VaultEntry> SortByUpdate(IEnumerable<VaultEntry> entries) =>
        entries.OrderByDescending(entry => entry.UpdatedAtUtc)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);

    private async Task AddAsync()
    {
        var entry = _dialogs.EditEntry(null, _generator, _allFolders, SelectedFolderFilter?.FolderId);
        if (entry is not null && !_isLocked)
        {
            await SaveAndRefreshAsync(entry, "Credencial adicionada.");
        }
    }

    private async Task EditAsync()
    {
        var entry = _dialogs.EditEntry(SelectedEntry!.Copy(), _generator, _allFolders);
        if (entry is not null && !_isLocked)
        {
            await SaveAndRefreshAsync(entry, "Credencial atualizada.");
        }
    }

    private Task ToggleFavoriteAsync()
    {
        var entry = SelectedEntry!.Copy();
        entry.IsFavorite = !entry.IsFavorite;
        return SaveAndRefreshAsync(entry, entry.IsFavorite ? "Credencial adicionada aos favoritos." : "Credencial removida dos favoritos.");
    }

    private Task SaveAndRefreshAsync(VaultEntry entry, string message) => RunVaultActionAsync(async () =>
    {
        await _vault.SaveEntryAsync(entry);
        await RefreshAsync();
        SelectedEntry = Entries.FirstOrDefault(item => item.Id == entry.Id) ?? SelectedEntry;
        StatusMessage = message;
    }, "Não foi possível salvar");

    private async Task AddFolderAsync()
    {
        var folder = _dialogs.EditFolder(null);
        if (folder is null || _isLocked)
        {
            return;
        }

        await RunVaultActionAsync(async () =>
        {
            await _vault.SaveFolderAsync(folder);
            await RefreshAsync();
            if (!_isLocked)
            {
                SearchText = string.Empty;
                SelectedFolderFilter = FolderFilters.First(option => option.FolderId == folder.Id);
            }
            StatusMessage = $"Pasta ‘{folder.Name}’ criada.";
        }, "Não foi possível criar a pasta");
    }

    private async Task RenameFolderAsync()
    {
        var current = GetSelectedFolder();
        if (current is null)
        {
            return;
        }
        var updated = _dialogs.EditFolder(current.Copy());
        if (updated is null || _isLocked)
        {
            return;
        }

        await RunVaultActionAsync(async () =>
        {
            await _vault.SaveFolderAsync(updated);
            await RefreshAsync();
            StatusMessage = $"Pasta renomeada para ‘{updated.Name}’.";
        }, "Não foi possível renomear a pasta");
    }

    private async Task DeleteFolderAsync()
    {
        var folder = GetSelectedFolder();
        if (folder is null || !_dialogs.Confirm(
                $"Excluir a pasta ‘{folder.Name}’? As credenciais dela serão movidas para ‘Sem pasta’.",
                "Excluir pasta") || _isLocked)
        {
            return;
        }

        await RunVaultActionAsync(async () =>
        {
            await _vault.DeleteFolderAsync(folder.Id);
            await RefreshAsync();
            StatusMessage = $"Pasta ‘{folder.Name}’ excluída. As credenciais foram preservadas.";
        }, "Não foi possível excluir a pasta");
    }

    private VaultFolder? GetSelectedFolder() => _allFolders.FirstOrDefault(folder => folder.Id == SelectedFolderFilter?.FolderId);
    private bool CanChangeVault() => !_isLocked && !IsBusy;
    private bool CanManageSelectedFolder() => IsFolderSelected && CanChangeVault();

    private async Task DeleteAsync()
    {
        var entry = SelectedEntry;
        if (entry is null || !_dialogs.Confirm($"Excluir ‘{entry.Name}’ do cofre?", "Excluir credencial") || _isLocked)
        {
            return;
        }

        await RunVaultActionAsync(async () =>
        {
            await _vault.DeleteEntryAsync(entry.Id);
            await RefreshAsync();
            StatusMessage = "Credencial excluída.";
        }, "Não foi possível excluir");
    }

    private async Task CopyAsync(string? value, string confirmation)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }
        try
        {
            await _dialogs.CopySensitiveAsync(value);
            StatusMessage = $"{confirmation} A cópia expira em até 30 segundos.";
            Touch();
        }
        catch (ExternalException)
        {
            StatusMessage = "A área de transferência está ocupada. Tente novamente.";
        }
    }

    private async Task BackupAsync()
    {
        var path = _dialogs.ChooseBackupToSave();
        if (path is null || _isLocked)
        {
            return;
        }
        await RunVaultActionAsync(async () =>
        {
            await _vault.ExportBackupAsync(path);
            StatusMessage = "Backup cifrado exportado.";
        }, "Falha no backup");
    }

    private Task SynchronizeAsync() => RunVaultActionAsync(async () =>
    {
        var result = await _sync.SynchronizeAsync();
        if (_isLocked)
        {
            return;
        }
        StatusMessage = result.Message;
        if (result.Status == SyncStatus.Downloaded)
        {
            await RefreshAsync();
        }
        if (result.Status is SyncStatus.Disabled or SyncStatus.Conflict)
        {
            _dialogs.ShowMessage(result.Message, "Sincronização", result.Status == SyncStatus.Conflict);
        }
    }, "Falha na sincronização");

    private Task SettingsAsync() =>
        RunVaultActionAsync(() => _dialogs.ShowSettingsAsync(_vault, _sync), "Falha nas configurações");

    private async Task RunVaultActionAsync(Func<Task> action, string errorTitle)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            if (!_isLocked)
            {
                _dialogs.ShowMessage(exception.Message, errorTitle, true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyVaultCommands()
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
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        GeneratorCommand.NotifyCanExecuteChanged();
    }

    public async Task LockAsync(string? message = null)
    {
        if (_isLocked)
        {
            return;
        }
        _isLocked = true;
        ExternalVaults?.Stop();
        _vault.Lock();
        HidePassword();
        SelectedEntry = null;
        _allEntries = [];
        _allFolders = [];
        Entries.Clear();
        FolderFilters = [];
        _selectedFolderFilter = null;
        OnPropertyChanged(nameof(SelectedFolderFilter));
        OnPropertyChanged(nameof(TotalEntryCount));
        NotifyFolderScope();
        SearchText = string.Empty;
        _dialogs.CloseVaultDialog();
        await _onLocked(message);
    }
}

public enum FolderFilterKind { All, Favorites, Recent, Unfiled, Folder }

public sealed record FolderFilterOption(FolderFilterKind Kind, Guid? FolderId, string Name, int Count)
{
    public string GroupName => Kind is FolderFilterKind.Unfiled or FolderFilterKind.Folder ? "Pastas" : "Visões";
    public string NavigationName => Kind == FolderFilterKind.All ? "Todas" : Name;
}

public enum EntrySort { Default, Name, Updated }

public sealed record EntrySortOption(EntrySort Sort, string Label);
