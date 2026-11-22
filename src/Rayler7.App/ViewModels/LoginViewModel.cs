using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rayler7.App.Services;
using Rayler7.Infrastructure.Security;

namespace Rayler7.App.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _onLoggedIn;

    public LoginViewModel(AppServices services, Action onLoggedIn)
    {
        _services = services;
        _onLoggedIn = onLoggedIn;
        IsFirstRun = !services.Auth.HasAnyUser();
    }

    public bool IsFirstRun { get; private set; }

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>A senha chega pela tela (PasswordBox não permite vínculo direto, por segurança).</summary>
    public async Task LoginAsync(string password)
    {
        Error = null;
        IsBusy = true;
        try
        {
            var user = Username;
            var result = await Task.Run(() => _services.Auth.Login(user, password));
            switch (result.Outcome)
            {
                case LoginOutcome.Success:
                    _services.CurrentUser = result.User;
                    _onLoggedIn();
                    break;
                case LoginOutcome.LockedOut:
                    Error = $"Acesso bloqueado temporariamente por excesso de tentativas. Tente novamente às {result.LockedUntil!.Value.ToLocalTime():HH:mm}.";
                    break;
                default:
                    Error = "Usuário ou senha incorretos.";
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CreateAdminAsync(string password, string confirm)
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(DisplayName))
        {
            Error = "Preencha o nome de usuário e o nome completo.";
            return;
        }

        if (password != confirm)
        {
            Error = "As senhas não conferem.";
            return;
        }

        IsBusy = true;
        try
        {
            var (u, d) = (Username, DisplayName);
            var admin = await Task.Run(() => _services.Auth.CreateFirstAdministrator(u, d, password));
            _services.CurrentUser = admin;
            _onLoggedIn();
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message.Split(" (Parameter", 2)[0];
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearError() => Error = null;
}
