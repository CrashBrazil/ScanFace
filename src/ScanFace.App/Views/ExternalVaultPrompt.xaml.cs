using System.Windows;
using System.Windows.Controls;
using ScanFace.Domain;
using ScanFace.Infrastructure;

namespace ScanFace.App.Views;

public partial class ExternalVaultPrompt : Window
{
    private bool _confirmPassword;
    private bool _hosted;
    private bool _checkCode;
    private string? _verificationCode;

    public ExternalVaultPrompt(string title, string description, string action)
    {
        InitializeComponent();
        Title = Heading.Text = title;
        Description.Text = description;
        Accept.Content = action;
    }

    public void ConfigureHosted(HostedVault? current)
    {
        _hosted = true;
        NameFields.Visibility = LanFields.Visibility = Visibility.Visible;
        VaultName.Text = current?.Content.Name ?? string.Empty;
        VisibleOnLan.IsChecked = current?.Settings.Discoverable ?? true;
        LoginInput.Text = current?.Settings.Login ?? string.Empty;
        if (current is not null)
        {
            PasswordLabel.Text = "Nova senha (opcional se o modo continuar o mesmo)";
        }
        Mode.ItemsSource = new[]
        {
            new Option<LanAccess>("Rede e senha", LanAccess.Password),
            new Option<LanAccess>("Login e senha", LanAccess.LoginAndPassword),
            new Option<LanAccess>("Rede, sem senha", LanAccess.Network)
        };
        Mode.SelectedIndex = current?.Settings.Access switch { LanAccess.LoginAndPassword => 1, LanAccess.Network => 2, _ => 0 };
        Duration.ItemsSource = new[]
        {
            new Option<int>("Até bloquear o ScanFace", 0),
            new Option<int>("15 minutos", 15),
            new Option<int>("1 hora", 60)
        };
        Duration.SelectedIndex = current?.Settings.DurationMinutes switch { 15 => 1, 60 => 2, _ => 0 };
    }

    public void ConfigureUsb(IEnumerable<UsbVolume> volumes)
    {
        Choices.Visibility = NameFields.Visibility = Visibility.Visible;
        ChoiceLabel.Text = "Unidade USB";
        Choice.ItemsSource = volumes.Select(volume => new Option<UsbVolume>(volume.DisplayName, volume));
        Choice.SelectedIndex = 0;
        ConfigurePassword(confirmation: true);
    }

    public void ConfigurePassword(bool confirmation = false)
    {
        _confirmPassword = confirmation;
        PasswordFields.Visibility = Visibility.Visible;
        ConfirmPasswordFields.Visibility = confirmation ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ConfigureJoin(LanVaultOffer offer, bool fromInvitation)
    {
        if (offer.Access != LanAccess.Network)
        {
            ConfigurePassword();
        }
        if (offer.Access == LanAccess.LoginAndPassword)
        {
            LoginFields.Visibility = Visibility.Visible;
        }
        if (!fromInvitation)
        {
            _verificationCode = offer.VerificationCode;
            ConfigureCode("Código de verificação do anfitrião");
            Code.MaxLength = 30;
        }
    }

    public void ConfigureCode(string label)
    {
        _checkCode = true;
        CodeLabel.Text = label;
        CodeFields.Visibility = Visibility.Visible;
    }

    public void ConfigureTransfer<T>(IEnumerable<Option<T>> candidates, bool publishing, string label)
    {
        Choices.Visibility = CopyName.Visibility = Visibility.Visible;
        CopyPublishing.Visibility = publishing ? Visibility.Visible : Visibility.Collapsed;
        ChoiceLabel.Text = label;
        Choice.ItemsSource = candidates;
        Choice.SelectedIndex = 0;
    }

    public string ResultName => VaultName.Text.Trim();
    public string ResultLogin => LoginInput.Text.Trim();
    public string ResultCode => Code.Text.Trim();
    public string TakePassword()
    {
        var value = PasswordInput.Password;
        PasswordInput.Clear();
        ConfirmPasswordInput.Clear();
        return value;
    }
    public bool ResultVisible => VisibleOnLan.IsChecked == true;
    public bool ResultCopyName => CopyName.IsChecked == true;
    public bool ResultCopyPublishing => CopyPublishing.IsChecked == true;
    public LanAccess ResultAccess => ((Option<LanAccess>)Mode.SelectedItem).Value;
    public int ResultDuration => ((Option<int>)Duration.SelectedItem).Value;
    public T SelectedChoice<T>() => ((Option<T>)Choice.SelectedItem).Value;

    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_hosted || Mode.SelectedItem is not Option<LanAccess> option)
        {
            return;
        }
        LoginFields.Visibility = option.Value == LanAccess.LoginAndPassword ? Visibility.Visible : Visibility.Collapsed;
        PasswordFields.Visibility = option.Value == LanAccess.Network ? Visibility.Collapsed : Visibility.Visible;
        ConfirmPasswordFields.Visibility = PasswordFields.Visibility;
        _confirmPassword = option.Value != LanAccess.Network;
    }

    private void AcceptClick(object sender, RoutedEventArgs e)
    {
        if (NameFields.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(ResultName))
        {
            Error.Text = "Informe o nome do cofre.";
        }
        else if (Choices.Visibility == Visibility.Visible && Choice.SelectedItem is null)
        {
            Error.Text = "Selecione uma opção.";
        }
        else if (_checkCode && string.IsNullOrWhiteSpace(ResultCode))
        {
            Error.Text = "Informe o código para continuar.";
        }
        else if (_verificationCode is not null && !string.Equals(
                     ResultCode.Replace(" ", string.Empty).Replace("-", string.Empty), _verificationCode, StringComparison.OrdinalIgnoreCase))
        {
            Error.Text = "O código não corresponde ao anunciado nesta conexão. Confira com o anfitrião.";
        }
        else if (_confirmPassword && PasswordInput.Password != ConfirmPasswordInput.Password)
        {
            Error.Text = "As senhas não coincidem.";
        }
        else
        {
            DialogResult = true;
        }
    }

    public sealed record Option<T>(string Label, T Value);
}
