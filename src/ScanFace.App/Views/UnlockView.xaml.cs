using System.Windows;
using System.Windows.Controls;
using ScanFace.App.ViewModels;

namespace ScanFace.App.Views;

public partial class UnlockView : UserControl
{
    public UnlockView() => InitializeComponent();

    private void MasterPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (DataContext is UnlockViewModel viewModel)
        {
            viewModel.MasterPassword = ((PasswordBox)sender).Password;
        }
    }
}
