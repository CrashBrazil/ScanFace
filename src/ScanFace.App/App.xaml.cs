using System.Net.Http;
using System.Windows;
using ScanFace.Application;
using ScanFace.App.Services;
using ScanFace.App.ViewModels;
using ScanFace.Infrastructure;

namespace ScanFace.App;

public partial class App : System.Windows.Application
{
    private VaultSession? _session;
    private HttpClient? _httpClient;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths();
        var repository = new SqliteVaultRepository(paths);
        var cryptography = new AesGcmCryptographyService();
        var deviceProtector = new DpapiDeviceKeyProtector();
        var hello = new WindowsHelloService();
        var backup = new JsonBackupFileService();
        _session = new VaultSession();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        var vault = new VaultApplicationService(repository, cryptography, deviceProtector, hello, backup, _session);
        var settings = new ProtectedSyncSettingsStore(paths, deviceProtector);
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
        _session?.Dispose();
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
