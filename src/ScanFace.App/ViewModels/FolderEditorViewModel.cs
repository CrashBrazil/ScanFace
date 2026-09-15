using ScanFace.App.Presentation;
using ScanFace.Domain;

namespace ScanFace.App.ViewModels;

public sealed class FolderEditorViewModel : ObservableObject
{
    private readonly Guid _id;
    private readonly DateTimeOffset _createdAtUtc;
    private string _name;
    private string _errorMessage = string.Empty;

    public FolderEditorViewModel(VaultFolder? folder)
    {
        _id = folder?.Id ?? Guid.NewGuid();
        _createdAtUtc = folder?.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        _name = folder?.Name ?? string.Empty;
        Title = folder is null ? "Nova pasta" : "Renomear pasta";
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public event Action<bool>? RequestClose;
    public string Title { get; }
    public VaultFolder? Result { get; private set; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    private void Save()
    {
        var name = Name.Trim();
        if (name.Length is < 1 or > 80)
        {
            ErrorMessage = "O nome deve ter entre 1 e 80 caracteres.";
            return;
        }

        Result = new VaultFolder
        {
            Id = _id,
            Name = name,
            CreatedAtUtc = _createdAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        RequestClose?.Invoke(true);
    }
}
