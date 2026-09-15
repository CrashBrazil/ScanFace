using System.Windows;
using System.Windows.Controls;
using ScanFace.App.ViewModels;

namespace ScanFace.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void ApiTokenChanged(object sender, RoutedEventArgs args)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.ApiToken = ((PasswordBox)sender).Password;
        }
    }

    private void NewMasterPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.NewMasterPassword = ((PasswordBox)sender).Password;
        }
    }

    private void ConfirmMasterPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.ConfirmMasterPassword = ((PasswordBox)sender).Password;
        }
    }
}
