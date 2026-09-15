using ScanFace.App.Presentation;

namespace ScanFace.App.ViewModels;

public sealed class BackupImportViewModel : ObservableObject
{
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _enableWindowsHello = true;

    public BackupImportViewModel()
    {
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public event Action<bool>? RequestClose;
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public bool EnableWindowsHello { get => _enableWindowsHello; set => SetProperty(ref _enableWindowsHello, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    private void Confirm()
    {
        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Informe a senha mestra usada no backup.";
            return;
        }
        RequestClose?.Invoke(true);
    }
}
