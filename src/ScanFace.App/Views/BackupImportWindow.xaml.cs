using System.Windows;
using System.Windows.Controls;
using ScanFace.App.ViewModels;

namespace ScanFace.App.Views;

public partial class BackupImportWindow : Window
{
    public BackupImportWindow() => InitializeComponent();

    private void PasswordChanged(object sender, RoutedEventArgs args)
    {
        if (DataContext is BackupImportViewModel viewModel)
        {
            viewModel.Password = ((PasswordBox)sender).Password;
        }
    }
}
