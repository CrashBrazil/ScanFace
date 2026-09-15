using System.Windows;
using Microsoft.Win32;
using ScanFace.Application;
using ScanFace.App.ViewModels;
using ScanFace.App.Views;
using ScanFace.Domain;

namespace ScanFace.App.Services;

public sealed class DialogService : IAppDialogService
{
    public Window? Owner { get; set; }

    public VaultEntry? EditEntry(
        VaultEntry? entry,
        PasswordGeneratorService generator,
        IReadOnlyList<VaultFolder> folders,
        Guid? defaultFolderId = null)
    {
        var viewModel = new EntryEditorViewModel(entry, generator, folders, defaultFolderId);
        var window = new EntryEditorWindow
        {
            Owner = Owner,
            DataContext = viewModel
        };
        viewModel.RequestClose += result => window.DialogResult = result;
        return window.ShowDialog() == true ? viewModel.Result : null;
    }

    public VaultFolder? EditFolder(VaultFolder? folder)
    {
        var viewModel = new FolderEditorViewModel(folder);
        var window = new FolderEditorWindow
        {
            Owner = Owner,
            DataContext = viewModel
        };
        viewModel.RequestClose += result => window.DialogResult = result;
        return window.ShowDialog() == true ? viewModel.Result : null;
    }

    public (string Password, bool EnableWindowsHello)? RequestBackupPassword()
    {
        var viewModel = new BackupImportViewModel();
        var window = new BackupImportWindow
        {
            Owner = Owner,
            DataContext = viewModel
        };
        viewModel.RequestClose += result => window.DialogResult = result;
        return window.ShowDialog() == true
            ? (viewModel.Password, viewModel.EnableWindowsHello)
            : null;
    }

    public string? ChooseBackupToOpen()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restaurar backup cifrado do ScanFace",
            Filter = "Backup ScanFace (*.scanface)|*.scanface|Todos os arquivos (*.*)|*.*"
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ChooseBackupToSave()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar backup cifrado do ScanFace",
            FileName = $"scanface-backup-{DateTime.Now:yyyy-MM-dd}.scanface",
            DefaultExt = ".scanface",
            Filter = "Backup ScanFace (*.scanface)|*.scanface"
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowMessage(string message, string title, bool isError = false) =>
        MessageBox.Show(Owner, message, title, MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);

    public async Task CopySensitiveAsync(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        Clipboard.SetText(value);
        await Task.Delay(TimeSpan.FromSeconds(30));
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == value)
                {
                    Clipboard.Clear();
                }
            }
            catch
            {
                // Another process can temporarily lock the Windows clipboard.
            }
        });
    }

    public async Task ShowSettingsAsync(VaultApplicationService vault, SyncCoordinator sync)
    {
        var viewModel = new SettingsViewModel(vault, sync);
        var window = new SettingsWindow
        {
            Owner = Owner,
            DataContext = viewModel
        };
        viewModel.RequestClose += () => window.Close();
        await viewModel.InitializeAsync();
        window.ShowDialog();
    }
}
