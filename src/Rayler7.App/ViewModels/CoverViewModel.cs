using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Rayler7.App.Services;
using Rayler7.Infrastructure.Projects;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.App.ViewModels;

public sealed record RecentProjectItem(ProjectInfo Project, string Details);

public sealed partial class CoverViewModel
{
    public const string TemplateFilter = "Planilhas suportadas (*.xlsx;*.xlsm;*.csv;*.tsv;*.txt)|*.xlsx;*.xlsm;*.csv;*.tsv;*.txt|Todos os arquivos (*.*)|*.*";

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly MainViewModel _main;

    public CoverViewModel(AppServices services, IDialogs dialogs, MainViewModel main)
    {
        _services = services;
        _dialogs = dialogs;
        _main = main;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        foreach (var p in services.Projects.Recent())
        {
            var template = services.Projects.Files(p.Id, ProjectService.RoleTemplate).LastOrDefault();
            var details = $"{template?.OriginalName ?? "Sem modelo enviado"} · Atualizado em {p.ModifiedAt.ToLocalTime().ToString("g", pt)}";
            Recent.Add(new RecentProjectItem(p, details));
        }
    }

    public ObservableCollection<RecentProjectItem> Recent { get; } = [];

    public bool HasRecent => Recent.Count > 0;

    public bool HasNoRecent => Recent.Count == 0;

    public string DataFolder => Path.Combine(_services.DataRoot, "projetos");

    [RelayCommand]
    private void StartNew() => _main.NewProjectCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasRecent))]
    private void ContinueLatest() => _main.OpenProject(Recent[0].Project);

    [RelayCommand]
    private void Open(RecentProjectItem item) => _main.OpenProject(item.Project);

    [RelayCommand]
    private void OpenSpreadsheet()
    {
        var path = _dialogs.OpenFile("Abrir planilha-modelo", TemplateFilter);
        if (path is null || _services.CurrentUser is null)
        {
            return;
        }

        try
        {
            var analysis = SpreadsheetAnalyzer.Analyze(path);
            var p = _services.Projects.Create(Path.GetFileNameWithoutExtension(path), _services.CurrentUser.Username);
            var file = _services.Projects.ImportFile(p, path, ProjectService.RoleTemplate);
            var ws = new ProjectWorkspace(p) { TemplateFile = file, TemplateAnalysis = analysis };
            _main.OpenWorkspace(ws);
            _main.Status($"Modelo carregado: {file.OriginalName}");
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _dialogs.Info(ex.Message, "Não foi possível abrir a planilha");
        }
    }
}
