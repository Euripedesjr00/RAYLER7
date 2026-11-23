using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rayler7.App.Services;
using Rayler7.Infrastructure.Projects;

namespace Rayler7.App.ViewModels;

public enum AppStage
{
    Login,
    Cover,
    Stage1,
    Stage2,
    Stage3,
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;

    public MainViewModel(AppServices services, IDialogs dialogs)
    {
        _services = services;
        _dialogs = dialogs;
        ShowLogin();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn), nameof(ShowHeaderNav))]
    private AppStage _stage;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject), nameof(ProjectName), nameof(CanStage2), nameof(CanStage3), nameof(ShowProjectBar))]
    private ProjectWorkspace? _workspace;

    [ObservableProperty]
    private string _statusText = "Pronto.";

    public bool IsLoggedIn => Stage != AppStage.Login;

    public bool ShowHeaderNav => IsLoggedIn;

    public bool HasProject => Workspace is not null;

    public bool ShowProjectBar => HasProject && Stage is AppStage.Stage1 or AppStage.Stage2 or AppStage.Stage3;

    public string ProjectName => Workspace?.Project.Name ?? string.Empty;

    public bool CanStage2 => Workspace?.TemplateFile is not null && Workspace.Stage1Confirmed;

    public bool CanStage3 => CanStage2 && Workspace!.ConferenceConfirmed;

    public string UserDisplay => _services.CurrentUser is { } u
        ? $"{u.DisplayName} · {(u.Role == Infrastructure.Security.UserRole.Administrador ? "Administrador" : "Usuário")}"
        : string.Empty;

    public void ShowLogin()
    {
        Workspace = null;
        Stage = AppStage.Login;
        CurrentPage = new LoginViewModel(_services, OnLoggedIn);
    }

    private void OnLoggedIn()
    {
        OnPropertyChanged(nameof(UserDisplay));
        GoCover();
        Status("Sessão iniciada.");
    }

    [RelayCommand]
    private void GoCover()
    {
        Stage = AppStage.Cover;
        CurrentPage = new CoverViewModel(_services, _dialogs, this);
        OnPropertyChanged(nameof(ShowProjectBar));
    }

    [RelayCommand]
    private void GoStage1()
    {
        if (Workspace is null)
        {
            return;
        }

        Stage = AppStage.Stage1;
        CurrentPage = new Stage1ViewModel(_services, _dialogs, this, Workspace);
        OnPropertyChanged(nameof(ShowProjectBar));
    }

    [RelayCommand]
    private void GoStage2()
    {
        if (!CanStage2)
        {
            return;
        }

        Stage = AppStage.Stage2;
        CurrentPage = new Stage2ViewModel(_services, _dialogs, this, Workspace!);
        OnPropertyChanged(nameof(ShowProjectBar));
    }

    [RelayCommand]
    private void GoStage3()
    {
        if (!CanStage3)
        {
            return;
        }

        Stage = AppStage.Stage3;
        CurrentPage = new Stage3ViewModel(_services, _dialogs, this, Workspace!);
        OnPropertyChanged(nameof(ShowProjectBar));
    }

    [RelayCommand]
    private void NewProject()
    {
        var user = _services.CurrentUser;
        if (user is null)
        {
            return;
        }

        var name = "Projeto " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
        var p = _services.Projects.Create(name, user.Username);
        OpenWorkspace(new ProjectWorkspace(p));
        Status($"Projeto criado: {p.Name}");
    }

    [RelayCommand]
    private void Logout()
    {
        if (_dialogs.Confirm("Deseja encerrar a sessão?", "RAYLER7"))
        {
            _services.CurrentUser = null;
            ShowLogin();
            Status("Sessão encerrada.");
        }
    }

    public void OpenWorkspace(ProjectWorkspace ws)
    {
        Workspace = ws;
        GoStage1();
    }

    public void OpenProject(ProjectInfo p)
    {
        var ws = new ProjectWorkspace(p);
        var template = _services.Projects.Files(p.Id, ProjectService.RoleTemplate).LastOrDefault();
        if (template is not null)
        {
            ws.TemplateFile = template;
            try
            {
                ws.TemplateAnalysis = Infrastructure.Spreadsheets.SpreadsheetAnalyzer.Analyze(template.StoredPath);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException)
            {
                Status($"Não foi possível reler o modelo: {ex.Message}");
            }
        }

        var (maps, confirmed) = _services.Mappings.Load(p.Id);
        foreach (var m in maps)
        {
            ws.Mapping.Set(m);
        }

        if (confirmed)
        {
            ws.Mapping.RestoreConfirmedState();
            ws.Stage1Confirmed = template is not null;
        }

        ws.ConferenceConfirmed = _services.Projects.GetConference(p.Id).Confirmed;
        OpenWorkspace(ws);
        Status($"Projeto aberto: {p.Name}");
    }

    public void RefreshProjectState()
    {
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(CanStage2));
        OnPropertyChanged(nameof(CanStage3));
    }

    public void Status(string text) => StatusText = $"{DateTime.Now:HH:mm} · {text}";
}
