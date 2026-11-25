using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _autoLockTimer.Stop();
        base.OnClosed(e);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or
            SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            LockFromSystem("Cofre bloqueado após a sessão do Windows ser bloqueada ou desconectada.");
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            LockFromSystem("Cofre bloqueado durante a suspensão do computador.");
        }
    }

    private void LockFromSystem(string message)
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.InvokeAsync(() => (DataContext as ShellViewModel)?.LockFromSystem(message), DispatcherPriority.Send);
        }
    }
}
