using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rayler7.App.Services;
using Rayler7.Core.Audit;
using Rayler7.Infrastructure.Export;

namespace Rayler7.App.ViewModels;

public sealed partial class Stage3ViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly MainViewModel _main;
    private readonly ProjectWorkspace _ws;
    private readonly IReadOnlyList<ExportSheet> _sheets = [];

    public Stage3ViewModel(AppServices services, IDialogs dialogs, MainViewModel main, ProjectWorkspace ws)
    {
        _services = services;
        _dialogs = dialogs;
        _main = main;
        _ws = ws;
        FilledPath = services.Projects.GetConference(ws.Project.Id).FilledPath;
        if (FilledPath is not null && File.Exists(FilledPath))
        {
            try
            {
                _sheets = ExportTable.Read(FilledPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                Error = $"Não foi possível ler a planilha preenchida: {ex.Message}";
            }
        }
        else
        {
            Error = "A planilha preenchida não foi encontrada. Volte à Etapa 2 e confirme a conferência final.";
        }

        foreach (var p in Enum.GetValues<StylePreset>())
        {
            Presets.Add(TableStyle.For(p));
        }

        ApplyPreset(TableStyle.For(StylePreset.Cientifico));
        Preview = BuildPreview();
    }

    public string? FilledPath { get; }

    public ObservableCollection<TableStyle> Presets { get; } = [];

    public IReadOnlyList<ExportFormatInfo> Formats => Exporters.Formats;

    public IReadOnlyList<string> Fonts { get; } = ["Calibri", "Segoe UI", "Times New Roman", "Arial", "Consolas", "Cambria"];

    public IReadOnlyList<double> FontSizes { get; } = [9, 10, 11, 12, 13, 14];

    public DataView? Preview { get; }

    public string PreviewInfo => _sheets.Count == 0 ? string.Empty : $"{_sheets[0].Name} · {_sheets[0].Rows.Count} linha(s) · {_sheets[0].Headers.Count} coluna(s) · {_sheets.Count} aba(s) com dados";

    [ObservableProperty]
    private StylePreset _preset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderBrush))]
    private string _headerBackground = "#1F3864";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTextBrush))]
    private string _headerText = "#FFFFFF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZebraBrush))]
    private string _zebraBackground = "#F2F5FA";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderBrushColor))]
    private string _borderColor = "#8EA9DB";

    [ObservableProperty]
    private string _bodyText = "#111111";

    [ObservableProperty]
    private string _fontName = "Calibri";

    [ObservableProperty]
    private double _fontSize = 10;

    [ObservableProperty]
    private bool _freezeHeader = true;

    [ObservableProperty]
    private bool _autoFit = true;

    [ObservableProperty]
    private bool _formattingConfirmed;

    [ObservableProperty]
    private bool _allSheets = true;

    [ObservableProperty]
    private bool _showAllFormats;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _lastExport;

    [ObservableProperty]
    private bool _exportCompleted;

    public bool IsOriginal => Preset == StylePreset.Original;

    public bool IsCustom => Preset == StylePreset.Personalizado;

    public Brush HeaderBrush => ToBrush(HeaderBackground, Colors.White);

    public Brush HeaderTextBrush => ToBrush(HeaderText, Colors.Black);

    public Brush ZebraBrush => ToBrush(ZebraBackground, Colors.White);

    public Brush BorderBrushColor => ToBrush(BorderColor, Colors.LightGray);

    [RelayCommand]
    private void ApplyPreset(TableStyle style)
    {
        Preset = style.Preset;
        if (style.Preset != StylePreset.Original)
        {
            HeaderBackground = style.HeaderBackground;
            HeaderText = style.HeaderText;
            ZebraBackground = style.ZebraBackground;
            BodyText = style.BodyText;
            BorderColor = style.BorderColor;
            FontName = style.FontName;
            FontSize = style.FontSize;
            FreezeHeader = style.FreezeHeader;
            AutoFit = style.AutoFitColumns;
        }

        FormattingConfirmed = false;
        OnPropertyChanged(nameof(IsOriginal));
        OnPropertyChanged(nameof(IsCustom));
    }

    [RelayCommand]
    private void ConfirmFormatting()
    {
        FormattingConfirmed = true;
        Audit("Confirmar formatação", Preset.ToString(), null);
        _main.Status("Formatação confirmada. Os dados não foram alterados.");
    }

    private TableStyle CurrentStyle() => new(Preset, HeaderBackground, HeaderText, ZebraBackground, BodyText, BorderColor,
        FontName, FontSize, true, FreezeHeader, AutoFit);

    [RelayCommand]
    private void Export(ExportFormat format)
    {
        Error = null;
        if (FilledPath is null || _sheets.Count == 0)
        {
            return;
        }

        if (!FormattingConfirmed && format == ExportFormat.Xlsx)
        {
            Error = "Confirme a formatação (botão CONFIRMAR FORMATAÇÃO) antes de gerar o Excel.";
            return;
        }

        var info = Exporters.Formats.First(f => f.Format == format);
        var baseName = $"{Safe(_ws.Project.Name)}_{DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture)}";
        var target = _dialogs.SaveFile($"Salvar {info.Label}", $"{info.Label}|*{info.Extension}", baseName + info.Extension);
        if (target is null)
        {
            return;
        }

        try
        {
            if (File.Exists(target))
            {
                File.Delete(target); // o usuário já confirmou a substituição na janela do Windows
            }

            var written = new List<string>();
            if (format == ExportFormat.Xlsx)
            {
                var report = XlsxStyler.Apply(FilledPath, target, CurrentStyle());
                written.Add(target);
                Audit("Exportar", Path.GetFileName(target), $"{report.CellsChecked} células conferidas; valores idênticos");
            }
            else
            {
                var sheets = AllSheets ? _sheets : [_sheets[0]];
                for (var i = 0; i < sheets.Count; i++)
                {
                    var path = i == 0 ? target : Path.Combine(Path.GetDirectoryName(target)!, $"{Path.GetFileNameWithoutExtension(target)}_{Safe(sheets[i].Name)}{info.Extension}");
                    Exporters.Write(format, [sheets[i]], path);
                    written.Add(path);
                }

                Audit("Exportar", string.Join(", ", written.Select(Path.GetFileName)), info.Label);
            }

            LastExport = string.Join("\n", written);
            ExportCompleted = true; // a mensagem final só aparece depois do arquivo realmente gravado
            _main.Status($"Exportação concluída: {Path.GetFileName(target)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        var first = LastExport?.Split('\n').FirstOrDefault();
        if (first is not null && File.Exists(first))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{first}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void AddMoreData() => _main.GoStage2Command.Execute(null);

    [RelayCommand]
    private void ToggleFormats() => ShowAllFormats = !ShowAllFormats;

    private DataView? BuildPreview()
    {
        if (_sheets.Count == 0)
        {
            return null;
        }

        var s = _sheets[0];
        var t = new DataTable();
        for (var c = 0; c < s.Headers.Count; c++)
        {
            t.Columns.Add(new DataColumn($"c{c}", typeof(string)) { Caption = s.Headers[c] });
        }

        foreach (var r in s.Rows.Take(60))
        {
            t.Rows.Add(Enumerable.Range(0, s.Headers.Count).Select(i => (object)(i < r.Count ? r[i] : string.Empty)).ToArray());
        }

        return t.DefaultView;
    }

    private void Audit(string op, string target, string? detail) =>
        _services.Audit.Append(new AuditEntry(_ws.Project.Id, _services.CurrentUser?.Username ?? "?", DateTimeOffset.Now, op, target, null, detail, "Etapa 3", null));

    private static Brush ToBrush(string hex, Color fallback)
    {
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
        catch (FormatException)
        {
            return new SolidColorBrush(fallback);
        }
    }

    private static string Safe(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch));
}
