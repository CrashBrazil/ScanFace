using System.Windows;
using System.Windows.Controls;
using ScanFace.App.ViewModels;

namespace ScanFace.App.Views;

public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    private void MasterPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (DataContext is SetupViewModel viewModel)
        {
            viewModel.MasterPassword = ((PasswordBox)sender).Password;
        }
    }

    private void ConfirmPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (DataContext is SetupViewModel viewModel)
        {
            viewModel.ConfirmPassword = ((PasswordBox)sender).Password;
        }
    }
}
