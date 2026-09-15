using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using ScanFace.App.Presentation;
using ScanFace.App.ViewModels;
using ScanFace.App.Views;

namespace ScanFace.Tests;

public sealed class ViewBindingTests
{
    [Fact]
    public void Views_UseSafeBindingsForPasswordInputAndReadOnlyState()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new global::ScanFace.App.App();
                application.InitializeComponent();
                var viewModel = new SetupViewModel(null!, null!, () => Task.CompletedTask);
                var view = new SetupView { DataContext = viewModel };
                var masterPassword = Assert.IsType<PasswordBox>(view.FindName("MasterPasswordInput"));
                var confirmation = Assert.IsType<PasswordBox>(view.FindName("ConfirmPasswordInput"));
                var masterBinding = Assert.IsType<BindingExpression>(
                    masterPassword.GetBindingExpression(PasswordBoxHelper.BoundPasswordProperty));
                var confirmationBinding = Assert.IsType<BindingExpression>(
                    confirmation.GetBindingExpression(PasswordBoxHelper.BoundPasswordProperty));

                masterPassword.Password = "uma senha mestra com 25";
                confirmation.Password = "uma senha mestra com 25";

                Assert.Equal(masterPassword.Password, viewModel.MasterPassword);
                Assert.Equal(confirmation.Password, viewModel.ConfirmPassword);
                Assert.Same(masterBinding, masterPassword.GetBindingExpression(PasswordBoxHelper.BoundPasswordProperty));
                Assert.Same(confirmationBinding, confirmation.GetBindingExpression(PasswordBoxHelper.BoundPasswordProperty));

                var vaultView = new VaultView();
                var entryCountRun = Assert.IsType<Run>(vaultView.FindName("EntryCountRun"));
                var entryCountBinding = Assert.IsType<BindingExpression>(
                    entryCountRun.GetBindingExpression(Run.TextProperty));

                Assert.Equal(BindingMode.OneWay, entryCountBinding.ParentBinding.Mode);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "O teste WPF não terminou no tempo esperado.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

}
