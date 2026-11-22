using System.ComponentModel;
using System.Windows;
using Rayler7.App.ViewModels;

namespace Rayler7.App.Views;

public partial class LoginView
{
    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Sync();
        Loaded += (_, _) =>
        {
            Sync();
            if (Vm is { IsFirstRun: false })
            {
                UserBox.Focus();
            }
        };
    }

    private LoginViewModel? Vm => DataContext as LoginViewModel;

    private void Sync()
    {
        if (Vm is not { } vm)
        {
            return;
        }

        SetupPanel.Visibility = vm.IsFirstRun ? Visibility.Visible : Visibility.Collapsed;
        LoginPanel.Visibility = vm.IsFirstRun ? Visibility.Collapsed : Visibility.Visible;
        SetupButton.IsDefault = vm.IsFirstRun;
        LoginButton.IsDefault = !vm.IsFirstRun;
        vm.PropertyChanged -= OnVmChanged;
        vm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        ErrorBox.Visibility = string.IsNullOrEmpty(vm.Error) ? Visibility.Collapsed : Visibility.Visible;
        SetupButton.IsEnabled = LoginButton.IsEnabled = !vm.IsBusy;
    }

    private async void OnLogin(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        var password = ShowPassword.IsChecked == true ? LoginPasswordVisible.Text : LoginPassword.Password;
        await vm.LoginAsync(password);
        LoginPassword.Clear();
        LoginPasswordVisible.Clear();
    }

    private async void OnCreateAdmin(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            await vm.CreateAdminAsync(SetupPassword.Password, SetupConfirm.Password);
        }
    }

    private void OnShowPassword(object sender, RoutedEventArgs e)
    {
        if (ShowPassword.IsChecked == true)
        {
            LoginPasswordVisible.Text = LoginPassword.Password;
            LoginPassword.Visibility = Visibility.Collapsed;
            LoginPasswordVisible.Visibility = Visibility.Visible;
        }
        else
        {
            LoginPassword.Password = LoginPasswordVisible.Text;
            LoginPasswordVisible.Clear();
            LoginPasswordVisible.Visibility = Visibility.Collapsed;
            LoginPassword.Visibility = Visibility.Visible;
        }
    }
}
