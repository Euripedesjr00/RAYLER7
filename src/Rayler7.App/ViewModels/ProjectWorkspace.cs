using Rayler7.Core.Mapping;
using Rayler7.Infrastructure.Projects;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.App.ViewModels;

/// <summary>Estado do projeto aberto, compartilhado entre as etapas (sem recadastro de dados).</summary>
public sealed class ProjectWorkspace(ProjectInfo project)
{
    public ProjectInfo Project { get; set; } = project;

    public ProjectFile? TemplateFile { get; set; }

    public SpreadsheetAnalysis? TemplateAnalysis { get; set; }

    public int ActiveSheetIndex { get; set; }

    public MappingSet Mapping { get; } = new();

    public bool Stage1Confirmed { get; set; }

    public bool ConferenceConfirmed { get; set; }

    public SheetAnalysis? ActiveSheet =>
        TemplateAnalysis is { } a && ActiveSheetIndex < a.Sheets.Count ? a.Sheets[ActiveSheetIndex] : null;
}
