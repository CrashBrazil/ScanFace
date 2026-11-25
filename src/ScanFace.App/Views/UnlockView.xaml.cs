using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ScanFace.App.ViewModels;

namespace ScanFace.App.Views;

public partial class UnlockView : UserControl
{
    private UnlockViewModel? _model;

    public UnlockView() => InitializeComponent();

    private void MasterPasswordChanged(object sender, RoutedEventArgs args)
    {
        using var password = ((PasswordBox)sender).SecurePassword;
        if (_model is not null)
        {
            _model.HasPasswordInput = password.Length > 0;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachModel(DataContext as UnlockViewModel);
        FocusCurrentMethod();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => AttachModel(null);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => AttachModel(e.NewValue as UnlockViewModel);

    private void AttachModel(UnlockViewModel? model)
    {
        if (ReferenceEquals(_model, model))
        {
            return;
        }
        ClearPassword();
        if (_model is not null)
        {
            _model.ClearPasswordRequested -= ClearPassword;
        }
        _model = model;
        if (_model is not null)
        {
            _model.ClearPasswordRequested += ClearPassword;
        }
    }

    private void ClearPassword() => MasterPasswordInput?.Clear();

    private async void SubmitPassword(object sender, RoutedEventArgs e)
    {
        if (_model is { CanSubmitPassword: true } model)
        {
            await model.UnlockWithPasswordAsync(MasterPasswordInput.Password);
        }
    }

    private void OnReturnToHello(object sender, RoutedEventArgs e) => FocusCurrentMethod();
    private void OnPasswordInputChanged(object sender, DependencyPropertyChangedEventArgs e) => FocusCurrentMethod();

    private void FocusCurrentMethod()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded || Window.GetWindow(this)?.IsActive == false)
            {
                return;
            }
            if (MasterPasswordInput.IsVisible && MasterPasswordInput.IsEnabled)
            {
                MasterPasswordInput.Focus();
            }
            else if (HelloButton.IsVisible && HelloButton.IsEnabled)
            {
                HelloButton.Focus();
            }
        }, DispatcherPriority.Input);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is UnlockViewModel viewModel && viewModel.CancelHelloCommand.CanExecute(null))
        {
            viewModel.CancelHelloCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OpenWindowsSignIn(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:signinoptions") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            if (DataContext is UnlockViewModel viewModel)
            {
                viewModel.StatusMessage = "Abra Configurações do Windows → Contas → Opções de entrada.";
            }
        }
    }
}
