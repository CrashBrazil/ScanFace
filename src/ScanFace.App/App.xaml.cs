using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ScanFace.Application;
using ScanFace.App.Services;
using ScanFace.App.ViewModels;
using ScanFace.Infrastructure;

namespace ScanFace.App;

public partial class App : System.Windows.Application
{
    private VaultSession? _session;
    private HttpClient? _httpClient;
    private AppPaths? _paths;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        base.OnStartup(e);

        _paths = new AppPaths();
        var repository = new SqliteVaultRepository(_paths);
        var cryptography = new AesGcmCryptographyService();
        var deviceProtector = new DpapiDeviceKeyProtector();
        var hello = new WindowsHelloService();
        var backup = new JsonBackupFileService();
        _session = new VaultSession();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        var vault = new VaultApplicationService(repository, cryptography, deviceProtector, hello, backup, _session);
        var settings = new ProtectedSyncSettingsStore(_paths, deviceProtector);
        var sync = new SyncCoordinator(repository, settings, new HttpRemoteSyncClient(_httpClient), _session);
        var dialogs = new DialogService();
        var shell = new ShellViewModel(vault, sync, new PasswordGeneratorService(), dialogs);

        var window = new MainWindow { DataContext = shell };
        dialogs.Owner = window;
        MainWindow = window;
        window.Show();

        try
        {
            await shell.InitializeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Falha ao iniciar", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= HandleDispatcherUnhandledException;
        _session?.Dispose();
        _httpClient?.Dispose();
        base.OnExit(e);
    }

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        args.Handled = true;
        var logPath = TryWriteErrorLog(args.Exception);
        var logMessage = logPath is null
            ? string.Empty
            : $"\n\nOs detalhes técnicos foram salvos em:\n{logPath}";

        MessageBox.Show(
            $"O ScanFace encontrou um erro inesperado e precisa ser fechado.{logMessage}",
            "Erro inesperado",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    private string? TryWriteErrorLog(Exception exception)
    {
        try
        {
            _paths ??= new AppPaths();
            _paths.EnsureCreated();
            var entry = $"[{DateTimeOffset.Now:O}] {exception}\n\n";
            File.AppendAllText(_paths.ErrorLogPath, entry, Encoding.UTF8);
            return _paths.ErrorLogPath;
        }
        catch
        {
            return null;
        }
    }
}
