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
    private string _generatorErrorMessage = string.Empty;
    private bool _isFavorite;
    private bool _revealPassword;
    private int _generatorLength = 20;
    private bool _includeUppercase = true;
    private bool _includeLowercase = true;
    private bool _includeNumbers = true;
    private bool _includeSymbols = true;
    private int _minimumNumbers = 1;
    private int _minimumSymbols = 1;
    private bool _avoidAmbiguousCharacters = true;

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
        GeneratePasswordCommand = new RelayCommand(GeneratePassword);
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
    public string GeneratorErrorMessage { get => _generatorErrorMessage; set => SetProperty(ref _generatorErrorMessage, value); }
    public int GeneratorLength { get => _generatorLength; set => SetProperty(ref _generatorLength, value); }
    public bool IncludeUppercase { get => _includeUppercase; set => SetProperty(ref _includeUppercase, value); }
    public bool IncludeLowercase { get => _includeLowercase; set => SetProperty(ref _includeLowercase, value); }
    public bool IncludeNumbers { get => _includeNumbers; set => SetProperty(ref _includeNumbers, value); }
    public bool IncludeSymbols { get => _includeSymbols; set => SetProperty(ref _includeSymbols, value); }
    public int MinimumNumbers { get => _minimumNumbers; set => SetProperty(ref _minimumNumbers, value); }
    public int MinimumSymbols { get => _minimumSymbols; set => SetProperty(ref _minimumSymbols, value); }
    public bool AvoidAmbiguousCharacters { get => _avoidAmbiguousCharacters; set => SetProperty(ref _avoidAmbiguousCharacters, value); }
    public IReadOnlyList<int> MinimumOptions { get; } = Enumerable.Range(0, 11).ToArray();
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand GeneratePasswordCommand { get; }

    private void GeneratePassword()
    {
        try
        {
            Password = _generator.Generate(new PasswordGeneratorOptions
            {
                Length = GeneratorLength,
                IncludeUppercase = IncludeUppercase,
                IncludeLowercase = IncludeLowercase,
                IncludeNumbers = IncludeNumbers,
                IncludeSymbols = IncludeSymbols,
                MinimumNumbers = MinimumNumbers,
                MinimumSymbols = MinimumSymbols,
                AvoidAmbiguousCharacters = AvoidAmbiguousCharacters
            });
            GeneratorErrorMessage = string.Empty;
        }
        catch (ArgumentException exception)
        {
            GeneratorErrorMessage = exception.Message;
        }
    }

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
