using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using ScanFace.App.Presentation;
using ScanFace.App.Services;
using ScanFace.App.ViewModels;
using ScanFace.App.Views;
using ScanFace.Application;
using ScanFace.Domain;

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

                var unlockViewModel = new UnlockViewModel(null!, () => Task.CompletedTask);
                var unlockView = new UnlockView { DataContext = unlockViewModel };
                var unlockInput = Assert.IsType<PasswordBox>(unlockView.FindName("MasterPasswordInput"));
                Assert.False(unlockViewModel.CanSubmitPassword);
                unlockInput.Password = "senha fictícia";
                Assert.True(unlockViewModel.CanSubmitPassword);
                unlockViewModel.CancelForSystemLock("Sessão bloqueada");
                Assert.Empty(unlockInput.Password);
                Assert.False(unlockViewModel.CanSubmitPassword);

                var vaultView = new VaultView();
                var entryCountRun = Assert.IsType<Run>(vaultView.FindName("EntryCountRun"));
                var entryCountBinding = Assert.IsType<BindingExpression>(
                    entryCountRun.GetBindingExpression(Run.TextProperty));

                Assert.Equal(BindingMode.OneWay, entryCountBinding.ParentBinding.Mode);

                var folder = new VaultFolder { Name = "Trabalho" };
                var editorViewModel = new EntryEditorViewModel(
                    null,
                    new PasswordGeneratorService(),
                    [folder],
                    folder.Id);
                editorViewModel.GeneratorLength = 32;
                editorViewModel.GeneratePasswordCommand.Execute(null);
                var folderEditorViewModel = new FolderEditorViewModel(folder) { Name = "Pessoal" };
                folderEditorViewModel.SaveCommand.Execute(null);

                Assert.Equal(folder.Id, editorViewModel.SelectedFolder?.Id);
                Assert.Equal(32, editorViewModel.Password.Length);
                Assert.Equal("Pessoal", folderEditorViewModel.Result?.Name);

                var hostedPrompt = new ExternalVaultPrompt("Novo cofre", "", "Criar");
                hostedPrompt.ConfigureHosted(null);
                Assert.True(hostedPrompt.ResultVisible);
                Assert.Equal(LanAccess.Password, hostedPrompt.ResultAccess);
                var shared = ExternalVaultContent.Create("Cofre recebido") with
                {
                    Entries = [new VaultEntry { Name = "Entrada de teste", Password = "fixture-only" }]
                };
                var externalWindow = new ExternalVaultWindow(null!, new(ExternalVaultKind.Remote, shared.Name, ""), shared,
                    new DialogService(), CancellationToken.None);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(externalWindow.FindName("EditEntry")).Visibility);
                Assert.Equal("••••••••••••", Assert.IsType<TextBlock>(externalWindow.FindName("EntryPassword")).Text);
                _ = new PasskeyOptionsWindow();
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
