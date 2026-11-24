using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rayler7.App.Services;
using Rayler7.Core.Audit;
using Rayler7.Core.Extraction;
using Rayler7.Core.Semantics;
using Rayler7.Core.Mapping;
using Rayler7.Core.Selection;
using Rayler7.Infrastructure.Extraction;
using Rayler7.Infrastructure.Projects;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.App.ViewModels;

public enum ReviewFilter
{
    Todos,
    Pendentes,
    Duvidas,
    Rejeitados,
    Selecionados,
    NaoSelecionados,
}

public sealed partial class FileItem : ObservableObject
{
    public required ProjectFile File { get; init; }

    public string Name => File.OriginalName;

    public string Kind => Path.GetExtension(File.OriginalName).TrimStart('.').ToUpperInvariant();

    [ObservableProperty]
    private string _status = "Na fila";

    [ObservableProperty]
    private string? _detail;
}

public sealed class RecordRow(ExtractedRecord record, Stage2ViewModel owner) : ObservableObject
{
    public ExtractedRecord Record { get; } = record;

    public bool IsSelected
    {
        get => owner.Session.IsSelected(Record.Id);
        set => owner.SetSelected(this, value);
    }

    public bool CanSelect => Record.Status != RecordStatus.Rejected;

    public string Location => $"{(Record.Page is { } p ? $"p.{p} " : string.Empty)}l.{Record.Row} c.{Record.Column}";

    public string Target => Record.TargetColumn ?? "—";

    public string Original => Record.Value.Original;

    public string ToUse => Record.LeaveEmpty ? "[vazio]" : Record.EffectiveText;

    public string StatusLabel => Stage2ViewModel.StatusLabel(Record.Status);

    public string Warning => Record.Doubts.FirstOrDefault()?.Message ?? string.Empty;

    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed partial class DoubtRow(ExtractedRecord record, System.Windows.Media.ImageSource? crop) : ObservableObject
{
    public ExtractedRecord Record { get; } = record;

    /// <summary>Trecho da imagem original onde o valor foi lido.</summary>
    public System.Windows.Media.ImageSource? Crop { get; } = crop;

    public bool HasCrop => Crop is not null;

    public string Header => $"{Record.SourceFile} · {(Record.Page is { } p ? $"página {p}, " : string.Empty)}linha {Record.Row}, coluna {Record.Column} → destino {Record.TargetColumn ?? "nenhum"}";

    public string Identified => Record.Value.Original.Length == 0 ? "[vazio]" : Record.Value.Original;

    public string? Suggested => Record.Doubts.Select(d => d.SuggestedValue).FirstOrDefault(s => s is not null);

    public bool HasSuggestion => Suggested is not null;

    public string Reasons => string.Join(" ", Record.Doubts.Select(d => d.Message));

    [ObservableProperty]
    private string _editText = record.Value.Original;
}

public sealed partial class Stage2ViewModel : ObservableObject
{
    public const int PageSize = 100;
    private const string SourceFilter = "Arquivos de dados|*.xlsx;*.xlsm;*.xls;*.ods;*.csv;*.tsv;*.txt;*.docx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.tif;*.tiff;*.heic|Todos os arquivos (*.*)|*.*";
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly MainViewModel _main;
    private readonly ProjectWorkspace _ws;
    private readonly RecordStore _store;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<Guid, string> _sourcePathByRecord = [];

    public Stage2ViewModel(AppServices services, IDialogs dialogs, MainViewModel main, ProjectWorkspace ws)
    {
        _services = services;
        _dialogs = dialogs;
        _main = main;
        _ws = ws;
        _store = new RecordStore(services.Database);
        Session = new ReviewSession();

        foreach (var f in services.Projects.Files(ws.Project.Id, ProjectService.RoleSource))
        {
            // Arquivos que ficaram "pendentes" numa versão anterior voltam para a fila quando o formato passa a ser suportado.
            var nowSupported = f.Status == "pendente" && DocumentExtractor.Support(f.StoredPath) == SupportLevel.Supported;
            Files.Add(new FileItem
            {
                File = f,
                Status = nowSupported ? "Na fila" : FileStatusLabel(f.Status),
                Detail = nowSupported ? "Formato agora suportado: pronto para processar." : f.StatusMessage,
            });
        }

        var stored = _store.Load(ws.Project.Id);
        var pathByFile = Files.ToDictionary(f => f.File.Id, f => f.File.StoredPath);
        foreach (var s in stored)
        {
            if (s.FileId is { } fid && pathByFile.TryGetValue(fid, out var sp))
            {
                _sourcePathByRecord[s.Record.Id] = sp;
            }
        }

        Session.AddRecords(stored.Select(s => s.Record));
        foreach (var s in stored.Where(s => s.Selected && ReviewSession.IsEligible(s.Record)))
        {
            Session.Select(s.Record.Id);
        }

        (_, FilledPath) = services.Projects.GetConference(ws.Project.Id);
        Refresh();
    }

    public ReviewSession Session { get; }

    public ObservableCollection<FileItem> Files { get; } = [];

    public ObservableCollection<RecordRow> PageRows { get; } = [];

    public ObservableCollection<DoubtRow> DoubtRows { get; } = [];

    public IReadOnlyList<ReviewFilter> Filters { get; } = Enum.GetValues<ReviewFilter>();

    [ObservableProperty]
    private ReviewFilter _filter = ReviewFilter.Todos;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private int _pageIndex;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _processingText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _allowProceedWithPending;

    [ObservableProperty]
    private string? _filledPath;

    [ObservableProperty]
    private string? _error;

    public SelectionSummary Summary => Session.Summary();

    public int PageCount => Session.PageCount(PageSize);

    public string PageText => $"Página {PageIndex + 1} de {PageCount} · {Session.FilteredRecords().Count} registro(s) no filtro";

    public bool HasFilter => Session.HasActiveFilter;

    public bool AllSelected => Summary.AllEligibleSelected;

    public string SelectAllLabel => AllSelected ? "☐  DESMARCAR TODOS OS DADOS" : "☑  SELECIONAR TODOS OS DADOS";

    public string SelectFilteredLabel => $"☑  Selecionar só os filtrados ({Session.FilteredRecords().Count(ReviewSession.IsEligible)})";

    public int Queued => Files.Count(f => f.Status is "Na fila" or "Falha");

    public int Done => Files.Count(f => f.Status == "Processado");

    public int PendingFiles => Files.Count(f => f.Status == "Pendente");

    public int Failed => Files.Count(f => f.Status == "Falha");

    public bool HasFiles => Files.Count > 0;

    public bool HasRecords => Session.Records.Count > 0;

    public bool HasDoubts => DoubtRows.Count > 0;

    public bool HasFilled => FilledPath is not null;

    public int SelectedOpenDoubts => Session.SelectedRecords().Count(r => r.HasOpenDoubt);

    public bool NeedsAuthorization => SelectedOpenDoubts > 0;

    public bool CanProceed => !IsProcessing && Summary.Selected > 0 && (!NeedsAuthorization || AllowProceedWithPending);

    public string ConferenceText
    {
        get
        {
            var plan = FillPlanner.Plan(Session.SelectedRecords(), FirstFreeRow(), AllowProceedWithPending);
            return $"Linhas a acrescentar: {plan.RowsAdded}  ·  Células a preencher: {plan.Writes.Count}  ·  Mantidas vazias: {plan.EmptyKept}  ·  " +
                   $"Sem destino (ignoradas): {plan.ExcludedNoTarget}  ·  Dúvidas fora: {plan.ExcludedOpenDoubts}  ·  Dúvidas sinalizadas incluídas: {plan.FlaggedDoubtsIncluded}";
        }
    }

    public static string StatusLabel(RecordStatus s) => s switch
    {
        RecordStatus.Pending => "Pendente",
        RecordStatus.Confirmed => "Confirmado",
        RecordStatus.Doubtful => "Duvidoso",
        RecordStatus.Rejected => "Rejeitado",
        _ => "Corrigido",
    };

    private static string FileStatusLabel(string s) => s switch
    {
        "processado" => "Processado",
        "pendente" => "Pendente",
        "falha" => "Falha",
        _ => "Na fila",
    };

    partial void OnFilterChanged(ReviewFilter value) => ApplyFilter();

    partial void OnSearchChanged(string value) => ApplyFilter();

    partial void OnAllowProceedWithPendingChanged(bool value) => Refresh();

    private void ApplyFilter()
    {
        var term = Search.Trim();
        Func<ExtractedRecord, bool>? byStatus = Filter switch
        {
            ReviewFilter.Pendentes => r => r.Status == RecordStatus.Pending,
            ReviewFilter.Duvidas => r => r.HasOpenDoubt,
            ReviewFilter.Rejeitados => r => r.Status == RecordStatus.Rejected,
            ReviewFilter.Selecionados => r => Session.IsSelected(r.Id),
            ReviewFilter.NaoSelecionados => r => !Session.IsSelected(r.Id),
            _ => null,
        };
        Func<ExtractedRecord, bool>? byText = term.Length == 0
            ? null
            : r => r.Value.Original.Contains(term, StringComparison.OrdinalIgnoreCase)
                   || r.SourceFile.Contains(term, StringComparison.OrdinalIgnoreCase)
                   || r.FieldName.Contains(term, StringComparison.OrdinalIgnoreCase);
        Session.SetFilter(byStatus is null && byText is null ? null : r => (byStatus?.Invoke(r) ?? true) && (byText?.Invoke(r) ?? true));
        PageIndex = 0;
        Refresh();
    }

    public void SetSelected(RecordRow row, bool value)
    {
        if (!row.CanSelect)
        {
            return;
        }

        if (value)
        {
            Session.Select(row.Record.Id);
        }
        else
        {
            Session.Deselect(row.Record.Id);
        }

        _store.SaveSelection(_ws.Project.Id, Session.SelectedIds);
        RefreshCounters();
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        if (AllSelected)
        {
            Session.DeselectAll(SelectionScope.AllRecords);
        }
        else
        {
            Session.SelectAll(SelectionScope.AllRecords);
        }

        _store.SaveSelection(_ws.Project.Id, Session.SelectedIds);
        Refresh();
    }

    [RelayCommand]
    private void SelectFiltered()
    {
        Session.SelectAll(SelectionScope.FilteredOnly);
        _store.SaveSelection(_ws.Project.Id, Session.SelectedIds);
        Refresh();
    }

    [RelayCommand]
    private void DeselectFiltered()
    {
        Session.DeselectAll(SelectionScope.FilteredOnly);
        _store.SaveSelection(_ws.Project.Id, Session.SelectedIds);
        Refresh();
    }

    [RelayCommand]
    private void NextPage()
    {
        if (PageIndex + 1 < PageCount)
        {
            PageIndex++;
            Refresh();
        }
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (PageIndex > 0)
        {
            PageIndex--;
            Refresh();
        }
    }

    [RelayCommand]
    private void AddFiles()
    {
        AddPaths(_dialogs.OpenFiles("Selecionar arquivos de dados", SourceFilter));
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (DocumentExtractor.Support(path) == SupportLevel.Unsupported)
                {
                    _main.Status($"Formato não suportado: {Path.GetFileName(path)}");
                    continue;
                }

                var f = _services.Projects.ImportFile(_ws.Project, path, ProjectService.RoleSource);
                Files.Add(new FileItem { File = f });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Error = $"{Path.GetFileName(path)}: {ex.Message}";
            }
        }

        Refresh();
    }

    [RelayCommand]
    private void RemoveFile(FileItem item)
    {
        if (!_dialogs.Confirm($"Retirar \"{item.Name}\" do projeto? Os registros extraídos dele também saem da revisão. A cópia guardada é mantida.", "Remover arquivo"))
        {
            return;
        }

        RemoveOne(item);
        ReloadRecords();
    }

    [RelayCommand]
    private void RemoveAll()
    {
        if (Files.Count == 0 || !_dialogs.Confirm($"Retirar todos os {Files.Count} arquivos e seus registros da revisão?", "Remover todos os arquivos"))
        {
            return;
        }

        foreach (var f in Files.ToList())
        {
            RemoveOne(f);
        }

        ReloadRecords();
    }

    private void RemoveOne(FileItem item)
    {
        _store.DeleteForFile(_ws.Project.Id, item.File.Id);
        _services.Projects.RemoveFromQueue(item.File.Id);
        Audit("Remover arquivo de origem", item.Name, null, null);
        Files.Remove(item);
    }

    private void ReloadRecords()
    {
        _main.RefreshProjectState();
        _main.GoStage2Command.Execute(null); // recarrega a tela a partir do estado salvo
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task StartExtractionAsync()
    {
        var queue = Files.Where(f => f.Status is "Na fila" or "Falha").ToList();
        if (queue.Count == 0 || _ws.ActiveSheet is not { } sheet)
        {
            return;
        }

        IsProcessing = true;
        Error = null;
        _cts = new CancellationTokenSource();
        var headers = sheet.Headers;
        var mappings = _ws.Mapping.Items.ToList();

        // Linhas já existentes no modelo (abaixo do cabeçalho), para apontar dados repetidos.
        var templateRows = sheet.Preview.Skip(sheet.HeaderRow ?? 0)
            .Select(r => (IReadOnlyDictionary<int, string>)r.Select((v, i) => (v, i)).ToDictionary(x => x.i + 1, x => x.v.StartsWith('=') ? string.Empty : x.v))
            .ToList();
        var sw = Stopwatch.StartNew();
        var processed = 0;
        try
        {
            foreach (var item in queue)
            {
                if (_cts.IsCancellationRequested)
                {
                    break;
                }

                var remaining = queue.Count - processed;
                var eta = processed > 0
                    ? $" · estimativa aproximada: {FormatEta(sw.Elapsed / processed * remaining)}"
                    : " · a estimativa aparece após o primeiro arquivo";
                ProcessingText = $"Processando {item.Name} ({processed + 1} de {queue.Count}){eta}";
                Progress = (double)processed / queue.Count * 100;
                item.Status = "Processando";

                if (DocumentExtractor.Support(item.File.StoredPath) == SupportLevel.Pending)
                {
                    SetFile(item, "pendente", "Pendente", DocumentExtractor.PendingReason(item.File.StoredPath));
                    processed++;
                    continue;
                }

                try
                {
                    var token = _cts.Token;
                    var (records, warnings, pages) = await Task.Run(() =>
                    {
                        var output = DocumentExtractor.Extract(item.File.StoredPath, item.Name, token);
                        var all = new List<ExtractedRecord>();
                        var warns = new List<string>(output.Warnings);
                        foreach (var t in output.Tables)
                        {
                            var built = RecordBuilder.Build(t, headers, mappings);
                            all.AddRange(built.Records);
                            warns.AddRange(built.Warnings);
                        }

                        var previous = DuplicateDetector.RowsOf(Session.Records.Where(ReviewSession.IsEligible))
                            .Select(g => (IReadOnlyDictionary<int, string>)g.Where(r => r.TargetColumn is not null)
                                .GroupBy(r => ColumnLetters.ToIndex(r.TargetColumn!)).ToDictionary(x => x.Key, x => x.First().EffectiveText));
                        var dup = DuplicateDetector.Flag(all, templateRows.Concat(previous));
                        if (dup > 0)
                        {
                            warns.Insert(0, $"{dup} linha(s) repetida(s) — confira nas dúvidas");
                        }

                        _store.Append(_ws.Project.Id, item.File.Id, all);
                        return (all, warns, output.PagesAnalyzed);
                    }, token);

                    foreach (var r in records)
                    {
                        _sourcePathByRecord[r.Id] = item.File.StoredPath;
                    }

                    Session.AddRecords(records);
                    var doubts = records.Count(r => r.HasOpenDoubt);
                    var msg = $"{records.Count} dado(s) extraído(s){(doubts > 0 ? $" · {doubts} dúvida(s)" : string.Empty)}{(pages > 0 ? $" · {pages} pág." : string.Empty)}"
                              + (warnings.Count > 0 ? $" · {warnings[0]}" : string.Empty);
                    SetFile(item, "processado", "Processado", msg);
                }
                catch (OperationCanceledException)
                {
                    SetFile(item, "fila", "Na fila", "Interrompido pelo usuário; pode ser reprocessado.");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Um arquivo problemático nunca derruba o lote: fica marcado como falha e o resto continua.
                    App.LogError(ex);
                    SetFile(item, "falha", "Falha", $"Falha (pode reprocessar): {ex.Message}");
                }

                processed++;
                Refresh();
            }
        }
        finally
        {
            IsProcessing = false;
            Progress = 100;
            ProcessingText = _cts.IsCancellationRequested ? "Fila interrompida. O que já foi extraído está salvo." : "Processamento do lote concluído.";
            _cts.Dispose();
            _cts = null;
            Refresh();
            _main.Status(ProcessingText);
        }
    }

    private void SetFile(FileItem item, string dbStatus, string label, string message)
    {
        _services.Projects.SetFileStatus(item.File.Id, dbStatus, message);
        item.Status = label;
        item.Detail = message;
    }

    private static string FormatEta(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{Math.Max(1, (int)t.TotalSeconds)} s" : $"{(int)t.TotalMinutes} min {t.Seconds} s";

    // ----- Dúvidas: o sistema nunca decide sozinho -----
    [RelayCommand]
    private void ConfirmDoubt(DoubtRow row) => Decide(row, r => r.ConfirmAsIdentified(User, _ws.Project.Id, _services.Audit));

    [RelayCommand]
    private void UseSuggestion(DoubtRow row)
    {
        if (row.Suggested is { } s)
        {
            Decide(row, r => r.Correct(s, User, _ws.Project.Id, _services.Audit, "Alternativa sugerida aceita pelo usuário"));
        }
    }

    [RelayCommand]
    private void SaveCorrection(DoubtRow row) =>
        Decide(row, r => r.Correct(row.EditText, User, _ws.Project.Id, _services.Audit, "Correção manual na conferência"));

    [RelayCommand]
    private void LeaveEmpty(DoubtRow row) => Decide(row, r => r.MarkLeaveEmpty(User, _ws.Project.Id, _services.Audit));

    [RelayCommand]
    private void RejectDoubt(DoubtRow row)
    {
        // Linha repetida: rejeitar vale para a linha inteira, senão o resto dela iria para a planilha.
        if (row.Record.Doubts.Any(d => d.Kind == DoubtKind.DuplicateRow))
        {
            foreach (var other in Session.Records.Where(r => r.Id != row.Record.Id && r.SourceFile == row.Record.SourceFile
                                                             && r.Page == row.Record.Page && r.Row == row.Record.Row
                                                             && r.Status != RecordStatus.Rejected))
            {
                other.Reject(User, _ws.Project.Id, _services.Audit, "Linha repetida rejeitada na conferência");
                _store.UpdateState(other);
            }
        }

        Decide(row, r => r.Reject(User, _ws.Project.Id, _services.Audit, "Rejeitado na conferência"));
    }

    private void Decide(DoubtRow row, Action<ExtractedRecord> action)
    {
        action(row.Record);
        _store.UpdateState(row.Record);
        Session.RefreshAfterStatusChange();
        _store.SaveSelection(_ws.Project.Id, Session.SelectedIds);
        Refresh();
    }

    private string User => _services.CurrentUser?.Username ?? throw new InvalidOperationException("Sessão expirada.");

    // ----- Conferência final e geração da planilha preenchida -----
    private int FirstFreeRow() => (_ws.ActiveSheet?.RowCount ?? 0) + 1;

    [RelayCommand]
    private void ConfirmAndProceed()
    {
        if (!CanProceed || _ws.TemplateFile is null || _ws.ActiveSheet is null)
        {
            return;
        }

        var plan = FillPlanner.Plan(Session.SelectedRecords(), FirstFreeRow(), AllowProceedWithPending);
        if (!_dialogs.Confirm(
                $"Será gerada uma NOVA planilha com {plan.RowsAdded} linha(s) e {plan.Writes.Count} célula(s) preenchida(s) ao final dos dados do modelo.\n" +
                "O modelo original não é alterado. Nenhum número será arredondado, convertido ou calculado.\n\nConfirmar e prosseguir?",
                "CONFIRMAR E PROSSEGUIR PARA A GERAÇÃO"))
        {
            return;
        }

        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var isCsv = !Path.GetExtension(_ws.TemplateFile.StoredPath).StartsWith(".xls", StringComparison.OrdinalIgnoreCase);
            var output = Path.Combine(_ws.Project.Folder, "trabalho", $"planilha_preenchida_{stamp}{(isCsv ? ".csv" : ".xlsx")}");
            var writes = plan.Writes.Select(w => new CellWrite(_ws.ActiveSheet.Name, w.Row, w.Column, w.Text)).ToList();
            var report = isCsv
                ? CsvFiller.Fill(_ws.TemplateFile.StoredPath, output, writes)
                : TemplateFiller.Fill(_ws.TemplateFile.StoredPath, output, writes);
            if (!report.TemplateUnchanged)
            {
                throw new IOException("Verificação de integridade falhou: o modelo foi alterado.");
            }

            Audit("Gerar planilha preenchida", Path.GetFileName(output), null,
                $"{report.CellsWritten} células; {report.Conflicts.Count} conflito(s); dúvidas sinalizadas incluídas: {plan.FlaggedDoubtsIncluded}");
            FilledPath = output;
            _ws.ConferenceConfirmed = true;
            _services.Projects.SetConference(_ws.Project.Id, true, output);
            _main.RefreshProjectState();
            _main.Status($"Planilha preenchida gerada: {Path.GetFileName(output)} ({report.CellsWritten} células).");
            if (report.Conflicts.Count > 0)
            {
                _dialogs.Info($"{report.Conflicts.Count} célula(s) já preenchida(s) no modelo foram preservadas e não receberam dados novos.", "Conflitos preservados");
            }

            _main.GoStage3Command.Execute(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFilledFolder()
    {
        if (FilledPath is { } p && File.Exists(p))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void BackToStage1() => _main.GoStage1Command.Execute(null);

    private void Audit(string op, string target, string? oldV, string? newV) =>
        _services.Audit.Append(new AuditEntry(_ws.Project.Id, User, DateTimeOffset.Now, op, target, oldV, newV, "Etapa 2", null));

    private void Refresh()
    {
        if (PageIndex >= PageCount)
        {
            PageIndex = Math.Max(0, PageCount - 1);
        }

        PageRows.Clear();
        foreach (var r in Session.Page(PageIndex, PageSize))
        {
            PageRows.Add(new RecordRow(r, this));
        }

        DoubtRows.Clear();
        foreach (var r in Session.Records.Where(r => r.HasOpenDoubt).Take(200))
        {
            DoubtRows.Add(new DoubtRow(r, ImageCrops.Crop(_sourcePathByRecord.GetValueOrDefault(r.Id) is { } src ? PageImages.PreviewPath(src, r.Page) : null, r.Box)));
        }

        RefreshCounters();
    }

    private void RefreshCounters()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(SelectAllLabel));
        OnPropertyChanged(nameof(SelectFilteredLabel));
        OnPropertyChanged(nameof(Queued));
        OnPropertyChanged(nameof(Done));
        OnPropertyChanged(nameof(PendingFiles));
        OnPropertyChanged(nameof(Failed));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasRecords));
        OnPropertyChanged(nameof(HasDoubts));
        OnPropertyChanged(nameof(SelectedOpenDoubts));
        OnPropertyChanged(nameof(NeedsAuthorization));
        OnPropertyChanged(nameof(CanProceed));
        OnPropertyChanged(nameof(ConferenceText));
        OnPropertyChanged(nameof(HasFilled));
        foreach (var r in PageRows)
        {
            r.Refresh();
        }
    }

    partial void OnFilledPathChanged(string? value) => OnPropertyChanged(nameof(HasFilled));

    partial void OnIsProcessingChanged(bool value) => OnPropertyChanged(nameof(CanProceed));
}
