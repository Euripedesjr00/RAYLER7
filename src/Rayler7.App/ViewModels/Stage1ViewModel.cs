using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rayler7.App.Services;
using Rayler7.Core.Mapping;
using Rayler7.Infrastructure.Projects;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.App.ViewModels;

public sealed partial class MappingRow : ObservableObject
{
    public int ColumnIndex { get; init; }

    public string Letter => ColumnLetters.FromIndex(ColumnIndex);

    public string? Header { get; init; }

    [ObservableProperty]
    private string _meaning = string.Empty;

    [ObservableProperty]
    private bool _expectNumeric;

    [ObservableProperty]
    private string _dictionaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceLabel))]
    private MappingSource _source;

    public string SourceLabel => Source switch
    {
        MappingSource.Header => "Cabeçalho",
        MappingSource.Instruction => "Instrução",
        _ => "Manual",
    };
}

public sealed record SheetTab(int Index, string Name);

public sealed partial class Stage1ViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly MainViewModel _main;
    private readonly ProjectWorkspace _ws;

    public Stage1ViewModel(AppServices services, IDialogs dialogs, MainViewModel main, ProjectWorkspace ws)
    {
        _services = services;
        _dialogs = dialogs;
        _main = main;
        _ws = ws;
        _projectName = ws.Project.Name;
        _instructions = ws.Project.Instructions ?? string.Empty;
        _isMappingConfirmed = ws.Mapping.IsConfirmed;
        RefreshAnalysis();
    }

    [ObservableProperty]
    private string _projectName;

    [ObservableProperty]
    private string _instructions;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private bool _isMappingConfirmed;

    [ObservableProperty]
    private DataView? _preview;

    [ObservableProperty]
    private string _previewNote = string.Empty;

    public ObservableCollection<SheetTab> Sheets { get; } = [];

    public ObservableCollection<MappingRow> MappingRows { get; } = [];

    public ObservableCollection<string> RecognizedRules { get; } = [];

    public ObservableCollection<string> Unrecognized { get; } = [];

    public ObservableCollection<string> Ambiguities { get; } = [];

    public ObservableCollection<string> Notes { get; } = [];

    public bool HasTemplate => _ws.TemplateAnalysis is not null;

    public bool HasNoTemplate => !HasTemplate;

    public bool CanAdvance => HasTemplate && IsMappingConfirmed && Ambiguities.Count == 0;

    public string FileName => _ws.TemplateFile?.OriginalName ?? string.Empty;

    public string SheetsText => _ws.TemplateAnalysis is { } a ? $"{a.Sheets.Count} ({a.Format})" : string.Empty;

    public string ColumnsText => _ws.ActiveSheet?.ColumnCount.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0";

    public string RowsText => _ws.ActiveSheet is { } s
        ? (s.HeaderRow is { } h ? s.RowCount - h : s.RowCount).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "0";

    public string FilledText => _ws.ActiveSheet is { } s ? $"{s.FilledCells} / {s.EmptyCells}" : string.Empty;

    public string MergesText => _ws.ActiveSheet is { } s
        ? $"{s.MergedRanges.Count} mescl. · {(s.Formulas.Count > 0 ? $"{s.Formulas.Count} fórmula(s)" : "Sem fórmulas")}"
        : string.Empty;

    public bool HasMultipleSheets => Sheets.Count > 1;

    public bool HasRules => RecognizedRules.Count > 0;

    public bool HasUnrecognized => Unrecognized.Count > 0;

    public bool HasAmbiguities => Ambiguities.Count > 0;

    public bool HasNotes => Notes.Count > 0;

    public int ActiveSheetIndex => _ws.ActiveSheetIndex;

    [RelayCommand]
    private void SelectTemplate()
    {
        var path = _dialogs.OpenFile("Selecionar planilha-modelo", CoverViewModel.TemplateFilter);
        if (path is not null)
        {
            LoadTemplate(path);
        }
    }

    public void LoadTemplate(string path)
    {
        Error = null;
        if (_ws.TemplateFile is not null &&
            !_dialogs.Confirm(
                $"Este projeto já possui a planilha-modelo \"{_ws.TemplateFile.OriginalName}\". Substituí-la por \"{Path.GetFileName(path)}\" reiniciará o mapeamento. O arquivo anterior continua guardado. Autorizar a substituição?",
                "Substituir planilha-modelo existente?"))
        {
            return;
        }

        try
        {
            var analysis = SpreadsheetAnalyzer.Analyze(path);
            var file = _services.Projects.ImportFile(_ws.Project, path, ProjectService.RoleTemplate);
            if (_ws.TemplateFile is not null && _services.CurrentUser is { } u)
            {
                _services.Audit.Append(new Core.Audit.AuditEntry(_ws.Project.Id, u.Username, DateTimeOffset.Now,
                    "Substituir planilha-modelo", "modelo", _ws.TemplateFile.OriginalName, file.OriginalName, "usuário", null));
                if (_ws.Mapping.IsConfirmed)
                {
                    _ws.Mapping.Unlock(u.Username, _ws.Project.Id, _services.Audit, "Modelo substituído");
                }
            }

            _ws.TemplateFile = file;
            _ws.TemplateAnalysis = analysis;
            _ws.ActiveSheetIndex = 0;
            _ws.Stage1Confirmed = false;
            foreach (var m in _ws.Mapping.Items.ToList())
            {
                _ws.Mapping.Remove(m.ColumnIndex);
            }

            IsMappingConfirmed = false;
            RefreshAnalysis();
            _main.RefreshProjectState();
            _main.Status($"Modelo analisado: {file.OriginalName} (cópia guardada; o arquivo original não foi alterado)");
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void SelectSheet(SheetTab tab)
    {
        _ws.ActiveSheetIndex = tab.Index;
        RefreshAnalysis();
    }

    [RelayCommand]
    private void SaveName()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            return;
        }

        _services.Projects.Rename(_ws.Project.Id, ProjectName);
        _ws.Project = _ws.Project with { Name = ProjectName.Trim() };
        _main.RefreshProjectState();
    }

    [RelayCommand]
    private void InterpretInstructions()
    {
        var sheet = _ws.ActiveSheet;
        var result = InstructionParser.Parse(Instructions, sheet?.ColumnCount ?? 0);
        Replace(RecognizedRules, result.RecognizedRules);
        Replace(Unrecognized, result.Unrecognized);
        Replace(Ambiguities, result.Ambiguities);
        foreach (var p in result.Proposals)
        {
            var row = MappingRows.FirstOrDefault(r => r.ColumnIndex == p.ColumnIndex);
            if (row is null)
            {
                continue;
            }

            if (p.Meaning is not null)
            {
                row.Meaning = p.Meaning;
            }

            if (p.ValueDictionary.Count > 0)
            {
                row.DictionaryText = string.Join("; ", p.ValueDictionary.Select(kv => $"{kv.Key} = {kv.Value}"));
            }

            row.Source = MappingSource.Instruction;
        }

        _services.Projects.SaveInstructions(_ws.Project.Id, Instructions);
        if (IsMappingConfirmed && _services.CurrentUser is { } u)
        {
            _ws.Mapping.Unlock(u.Username, _ws.Project.Id, _services.Audit, "Novas instruções interpretadas");
            IsMappingConfirmed = false;
        }

        NotifyLists();
        _main.Status($"Instruções salvas no projeto. {result.Proposals.Count} associação(ões) sugerida(s); confira antes de confirmar.");
    }

    [RelayCommand]
    private void ConfirmMapping()
    {
        if (_services.CurrentUser is not { } u || _ws.ActiveSheet is null)
        {
            return;
        }

        if (Ambiguities.Count > 0)
        {
            Error = "Há instruções ambíguas. Ajuste o texto ou o mapeamento antes de confirmar.";
            return;
        }

        if (_ws.Mapping.IsConfirmed)
        {
            _ws.Mapping.Unlock(u.Username, _ws.Project.Id, _services.Audit, "Reconfirmação do mapeamento");
        }

        foreach (var row in MappingRows)
        {
            var meaning = string.IsNullOrWhiteSpace(row.Meaning) ? row.Header ?? row.Letter : row.Meaning.Trim();
            _ws.Mapping.Set(new ColumnMapping(row.ColumnIndex, row.Header, meaning, row.ExpectNumeric, ParseDictionary(row.DictionaryText), row.Source));
        }

        _ws.Mapping.Confirm(u.Username, _ws.Project.Id, _services.Audit);
        _services.Mappings.Save(_ws.Project.Id, _ws.Mapping.Items, confirmed: true);
        IsMappingConfirmed = true;
        Error = null;
        _main.Status("Mapeamento confirmado e travado. Alterações futuras exigem nova confirmação.");
    }

    [RelayCommand]
    private void UnlockMapping()
    {
        if (_services.CurrentUser is not { } u || !IsMappingConfirmed)
        {
            return;
        }

        if (_dialogs.Confirm("O mapeamento confirmado será destravado para edição. A alteração ficará registrada no histórico. Continuar?", "Editar mapeamento"))
        {
            _ws.Mapping.Unlock(u.Username, _ws.Project.Id, _services.Audit, "Edição solicitada pelo usuário");
            _services.Mappings.Save(_ws.Project.Id, _ws.Mapping.Items, confirmed: false);
            IsMappingConfirmed = false;
            _ws.Stage1Confirmed = false;
            _main.RefreshProjectState();
        }
    }

    [RelayCommand]
    private void ConfirmAndAdvance()
    {
        if (!CanAdvance)
        {
            Error = "Confirme o mapeamento das colunas antes de avançar.";
            return;
        }

        _ws.Stage1Confirmed = true;
        _main.RefreshProjectState();
        _main.GoStage2Command.Execute(null);
    }

    private static Dictionary<string, string> ParseDictionary(string text)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && kv[0].Length > 0)
            {
                dict[kv[0]] = kv[1];
            }
        }

        return dict;
    }

    private void RefreshAnalysis()
    {
        Sheets.Clear();
        Notes.Clear();
        MappingRows.Clear();
        Preview = null;
        if (_ws.TemplateAnalysis is { } a)
        {
            for (var i = 0; i < a.Sheets.Count; i++)
            {
                Sheets.Add(new SheetTab(i, a.Sheets[i].Name));
            }

            foreach (var w in a.Warnings)
            {
                Notes.Add(w);
            }

            if (_ws.ActiveSheet is { } s)
            {
                if (s.Formulas.Count > 0)
                {
                    Notes.Add($"Fórmulas preservadas: {string.Join("; ", s.Formulas.Take(5))}{(s.Formulas.Count > 5 ? " …" : string.Empty)}");
                }

                if (s.MergedRanges.Count > 0)
                {
                    Notes.Add($"Células mescladas mantidas: {string.Join(", ", s.MergedRanges.Take(8))}");
                }

                for (var c = 1; c <= s.ColumnCount; c++)
                {
                    var header = c - 1 < s.Headers.Count ? s.Headers[c - 1] : null;
                    var saved = _ws.Mapping.Get(c);
                    MappingRows.Add(new MappingRow
                    {
                        ColumnIndex = c,
                        Header = header,
                        Meaning = saved?.Meaning ?? header ?? string.Empty,
                        ExpectNumeric = saved?.ExpectNumeric ?? false,
                        DictionaryText = saved is null ? string.Empty : string.Join("; ", saved.ValueDictionary.Select(kv => $"{kv.Key} = {kv.Value}")),
                        Source = saved?.Source ?? MappingSource.Header,
                    });
                }

                Preview = BuildPreview(s);
                PreviewNote = s.RowCount > SpreadsheetAnalyzer.PreviewRows
                    ? $"Exibindo {SpreadsheetAnalyzer.PreviewRows} de {s.RowCount} linhas do modelo. Todas as linhas originais estão preservadas."
                    : $"{s.RowCount} linha(s) exibida(s). O arquivo original não é alterado.";
            }
        }

        OnPropertyChanged(string.Empty);
    }

    private static DataView BuildPreview(SheetAnalysis s)
    {
        var table = new DataTable();
        for (var c = 1; c <= s.ColumnCount; c++)
        {
            var header = c - 1 < s.Headers.Count ? s.Headers[c - 1] : string.Empty;
            table.Columns.Add(new DataColumn($"c{c}", typeof(string)) { Caption = $"{ColumnLetters.FromIndex(c)} · {header}" });
        }

        foreach (var row in s.Preview)
        {
            var values = new object[s.ColumnCount];
            for (var c = 0; c < s.ColumnCount; c++)
            {
                values[c] = c < row.Count ? row[c] : string.Empty;
            }

            table.Rows.Add(values);
        }

        return table.DefaultView;
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> items)
    {
        target.Clear();
        foreach (var i in items)
        {
            target.Add(i);
        }
    }

    private void NotifyLists()
    {
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(HasUnrecognized));
        OnPropertyChanged(nameof(HasAmbiguities));
        OnPropertyChanged(nameof(CanAdvance));
    }
}
