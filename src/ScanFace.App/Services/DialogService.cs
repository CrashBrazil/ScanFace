using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ScanFace.Application;
using ScanFace.App.ViewModels;
using ScanFace.App.Views;
using ScanFace.Domain;

namespace ScanFace.App.Services;

public sealed class DialogService : IAppDialogService
{
    private readonly List<Window> _vaultDialogs = [];
    private DispatcherTimer? _clipboardTimer;

    public Window? Owner { get; set; }
    internal Window? ActiveOwner => _vaultDialogs.LastOrDefault() ?? Owner;

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
        return ShowVaultDialog(window) == true ? viewModel.Result : null;
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
        return ShowVaultDialog(window) == true ? viewModel.Result : null;
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
        MessageBox.Show(ActiveOwner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowMessage(string message, string title, bool isError = false) =>
        MessageBox.Show(ActiveOwner, message, title, MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);

    public Task CopySensitiveAsync(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Task.CompletedTask;
        }

        Clipboard.SetText(value);
        var sequence = GetClipboardSequenceNumber();
        _clipboardTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clipboardTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _clipboardTimer = null;
            try
            {
                if (sequence != 0 && GetClipboardSequenceNumber() == sequence)
                {
                    Clipboard.Clear();
                }
            }
            catch (ExternalException)
            {
                // Another process can temporarily lock the Windows clipboard.
            }
        };
        timer.Start();
        return Task.CompletedTask;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    public void ShowGenerator(PasswordGeneratorService generator) =>
        ShowVaultDialog(new GeneratorWindow
        {
            Owner = Owner,
            DataContext = new GeneratorViewModel(generator, CopySensitiveAsync)
        });

    public void CloseVaultDialog()
    {
        foreach (var dialog in _vaultDialogs.ToArray().Reverse())
        {
            dialog.Close();
        }
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
        if (vault.IsUnlocked)
        {
            ShowVaultDialog(window);
        }
    }

    internal bool? ShowVaultDialog(Window window)
    {
        window.Owner = ActiveOwner;
        _vaultDialogs.Add(window);
        window.PreviewKeyDown += TouchVault;
        window.PreviewMouseDown += TouchVault;
        window.PreviewMouseWheel += TouchVault;
        try
        {
            return window.ShowDialog();
        }
        finally
        {
            window.PreviewKeyDown -= TouchVault;
            window.PreviewMouseDown -= TouchVault;
            window.PreviewMouseWheel -= TouchVault;
            _vaultDialogs.Remove(window);
        }
    }

    private void TouchVault(object sender, InputEventArgs e) =>
        (Owner?.DataContext as ShellViewModel)?.Touch();
}
