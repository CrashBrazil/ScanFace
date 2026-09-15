using System.Windows;
using System.Windows.Controls;

namespace ScanFace.App.Presentation;

public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword",
        typeof(string),
        typeof(PasswordBoxHelper),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject obj) => (string)obj.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject obj, string value) => obj.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PasswordBox passwordBox)
        {
            return;
        }

        var password = args.NewValue as string ?? string.Empty;
        if (passwordBox.Password != password)
        {
            passwordBox.Password = password;
        }
    }
}
