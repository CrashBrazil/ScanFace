using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScanFace.App.Services;
using ScanFace.App.ViewModels;

namespace ScanFace.App.Views;

public partial class VaultView : UserControl
{
    private Window? _window;

    public VaultView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.Deactivated += OnWindowDeactivated;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }
        (DataContext as VaultViewModel)?.HidePassword();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) =>
        (DataContext as VaultViewModel)?.HidePassword();

    private void OnClearSearch(object sender, RoutedEventArgs e) => SearchInput.Focus();

    private void OnExternalMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private async void OnCreateNetworkVault(object sender, RoutedEventArgs e) => await RunExternalAsync(vaults => vaults.CreateHostedAsync());
    private async void OnCreateUsbVault(object sender, RoutedEventArgs e) => await RunExternalAsync(vaults => vaults.CreateUsbAsync());
    private async void OnJoinNetworkVault(object sender, RoutedEventArgs e) => await RunExternalAsync(vaults => vaults.JoinAsync());

    private void OnPasskeyOptions(object sender, RoutedEventArgs e) =>
        (DataContext as VaultViewModel)?.ExternalVaults?.ShowPasskeys();

    private async void OnOpenExternalVault(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExternalVaultSource source })
        {
            await RunExternalAsync(vaults => vaults.OpenAsync(source));
        }
    }

    private async Task RunExternalAsync(Func<ExternalVaultWorkspace, Task> action)
    {
        if (DataContext is VaultViewModel { ExternalVaults: { } vaults } model && !model.IsBusy)
        {
            var revision = vaults.PersonalRevision;
            await action(vaults);
            if (revision != vaults.PersonalRevision)
            {
                await model.RefreshAfterTransferAsync();
            }
        }
    }

    private void OnEntryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(EntryList, source) is ListBoxItem &&
            DataContext is VaultViewModel model && TryExecute(model.EditCommand))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not VaultViewModel model)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = e.Key switch
            {
                Key.F => FocusSearch(),
                Key.N => TryExecute(model.AddCommand),
                Key.G => TryExecute(model.GeneratorCommand),
                Key.L => TryExecute(model.LockCommand),
                _ => e.Handled
            };
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N)
        {
            e.Handled = TryExecute(model.AddFolderCommand);
        }
        else if (e.Key == Key.Escape && model.IsPasswordVisible)
        {
            model.HidePassword();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchInput.IsKeyboardFocusWithin)
        {
            e.Handled = TryExecute(model.ClearSearchCommand);
        }
    }

    private bool FocusSearch()
    {
        SearchInput.Focus();
        SearchInput.SelectAll();
        return true;
    }

    private static bool TryExecute(ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }
}
