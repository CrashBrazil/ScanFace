using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ScanFace.App.Presentation;
using ScanFace.Application;

namespace ScanFace.App.ViewModels;

public sealed class GeneratorViewModel : ObservableObject
{
    private readonly PasswordGeneratorService _generator;
    private readonly Func<string, Task> _copy;
    private int _length = 20;
    private bool _includeNumbers = true;
    private bool _includeSymbols = true;
    private bool _avoidAmbiguous = true;
    private string _password = string.Empty;
    private string _status = string.Empty;

    public GeneratorViewModel(PasswordGeneratorService generator, Func<string, Task> copy)
    {
        _generator = generator;
        _copy = copy;
        GenerateCommand = new RelayCommand(Generate);
        CopyCommand = new AsyncRelayCommand(CopyAsync);
        Generate();
    }

    public int Length { get => _length; set => UpdateOption(ref _length, Math.Clamp(value, 8, 128)); }
    public bool IncludeNumbers { get => _includeNumbers; set => UpdateOption(ref _includeNumbers, value); }
    public bool IncludeSymbols { get => _includeSymbols; set => UpdateOption(ref _includeSymbols, value); }
    public bool AvoidAmbiguous { get => _avoidAmbiguous; set => UpdateOption(ref _avoidAmbiguous, value); }
    public string Password { get => _password; private set => SetProperty(ref _password, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public RelayCommand GenerateCommand { get; }
    public AsyncRelayCommand CopyCommand { get; }

    private void UpdateOption<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (SetProperty(ref field, value, property))
        {
            Generate();
        }
    }

    private void Generate()
    {
        Password = _generator.Generate(new PasswordGeneratorOptions
        {
            Length = Length,
            IncludeNumbers = IncludeNumbers,
            IncludeSymbols = IncludeSymbols,
            MinimumNumbers = IncludeNumbers ? 1 : 0,
            MinimumSymbols = IncludeSymbols ? 1 : 0,
            AvoidAmbiguousCharacters = AvoidAmbiguous
        });
        Status = string.Empty;
    }

    private async Task CopyAsync()
    {
        try
        {
            await _copy(Password);
            Status = "Senha copiada por até 30 segundos.";
        }
        catch (ExternalException)
        {
            Status = "A área de transferência está ocupada. Tente novamente.";
        }
    }
}
