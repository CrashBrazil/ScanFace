using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ScanFace.App.ViewModels;

namespace ScanFace.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _autoLockTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public MainWindow()
    {
        InitializeComponent();
        _autoLockTimer.Tick += (_, _) => (DataContext as ShellViewModel)?.CheckAutoLock();
        _autoLockTimer.Start();
    }

    private void OnUserActivity(object sender, InputEventArgs e) =>
        (DataContext as ShellViewModel)?.Touch();
}
