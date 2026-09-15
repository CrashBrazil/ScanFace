using System.Windows;
using System.Windows.Controls;
using ScanFace.App.ViewModels;

namespace ScanFace.App.Views;

public partial class EntryEditorWindow : Window
{
    public EntryEditorWindow() => InitializeComponent();

    private void PasswordChanged(object sender, RoutedEventArgs args)
    {
        if (DataContext is EntryEditorViewModel viewModel)
        {
            viewModel.Password = ((PasswordBox)sender).Password;
        }
    }
}
