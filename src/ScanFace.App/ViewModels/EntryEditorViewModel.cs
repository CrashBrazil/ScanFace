using ScanFace.Application;
using ScanFace.App.Presentation;
using ScanFace.Domain;

namespace ScanFace.App.ViewModels;

public sealed class EntryEditorViewModel : ObservableObject
{
    private readonly PasswordGeneratorService _generator;
    private readonly Guid _id;
    private readonly DateTimeOffset _createdAtUtc;
    private string _name = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _website = string.Empty;
    private string _notes = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isFavorite;
    private bool _revealPassword;

    public EntryEditorViewModel(VaultEntry? entry, PasswordGeneratorService generator)
    {
        _generator = generator;
        _id = entry?.Id ?? Guid.NewGuid();
        _createdAtUtc = entry?.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        Name = entry?.Name ?? string.Empty;
        Username = entry?.Username ?? string.Empty;
        Password = entry?.Password ?? string.Empty;
        Website = entry?.Website ?? string.Empty;
        Notes = entry?.Notes ?? string.Empty;
        IsFavorite = entry?.IsFavorite ?? false;
        Title = entry is null ? "Nova credencial" : "Editar credencial";

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        GeneratePasswordCommand = new RelayCommand(() => Password = _generator.Generate(20, true));
    }

    public event Action<bool>? RequestClose;
    public string Title { get; }
    public VaultEntry? Result { get; private set; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string Website { get => _website; set => SetProperty(ref _website, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public bool IsFavorite { get => _isFavorite; set => SetProperty(ref _isFavorite, value); }
    public bool RevealPassword { get => _revealPassword; set => SetProperty(ref _revealPassword, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand GeneratePasswordCommand { get; }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Informe um nome para a credencial.";
            return;
        }
        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Informe ou gere uma senha.";
            return;
        }

        Result = new VaultEntry
        {
            Id = _id,
            Name = Name.Trim(),
            Username = Username.Trim(),
            Password = Password,
            Website = Website.Trim(),
            Notes = Notes.Trim(),
            IsFavorite = IsFavorite,
            CreatedAtUtc = _createdAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        RequestClose?.Invoke(true);
    }
}
