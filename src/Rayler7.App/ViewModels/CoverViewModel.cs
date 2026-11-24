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
    public const string TemplateFilter = "Modelos suportados|*.xlsx;*.xlsm;*.xls;*.ods;*.csv;*.tsv;*.txt;*.docx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp;*.heic|Planilhas (*.xlsx;*.xlsm;*.xls;*.ods;*.csv)|*.xlsx;*.xlsm;*.xls;*.ods;*.csv|Documentos e fotos (*.pdf;*.docx;*.jpg;*.png)|*.pdf;*.docx;*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp;*.heic|Todos os arquivos (*.*)|*.*";

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

        // Cria o projeto e entrega à Etapa 1, que analisa (ou converte) o modelo e mostra o resultado.
        var p = _services.Projects.Create(Path.GetFileNameWithoutExtension(path), _services.CurrentUser.Username);
        _main.OpenWorkspace(new ProjectWorkspace(p));
        if (_main.CurrentPage is Stage1ViewModel stage1)
        {
            stage1.LoadTemplate(path);
        }
    }
}
