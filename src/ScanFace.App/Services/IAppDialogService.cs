using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.App.Services;

public interface IAppDialogService
{
    VaultEntry? EditEntry(VaultEntry? entry, PasswordGeneratorService generator);
    (string Password, bool EnableWindowsHello)? RequestBackupPassword();
    string? ChooseBackupToOpen();
    string? ChooseBackupToSave();
    bool Confirm(string message, string title);
    void ShowMessage(string message, string title, bool isError = false);
    Task CopySensitiveAsync(string value);
    Task ShowSettingsAsync(VaultApplicationService vault, SyncCoordinator sync);
}
