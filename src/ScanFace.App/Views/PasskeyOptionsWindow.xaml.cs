using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace ScanFace.App.Views;

public partial class PasskeyOptionsWindow : Window
{
    public PasskeyOptionsWindow() => InitializeComponent();

    private void OpenWindowsClick(object sender, RoutedEventArgs e) => Open("ms-settings:signinoptions");
    private void OpenGoogleClick(object sender, RoutedEventArgs e) => Open("https://myaccount.google.com/signinoptions/passkeys");

    private void Open(string destination)
    {
        try
        {
            Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            Status.Text = "O Windows não conseguiu abrir a página de configuração.";
        }
    }
}
