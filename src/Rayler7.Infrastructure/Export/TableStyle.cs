using ClosedXML.Excel;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.Infrastructure.Export;

public enum StylePreset
{
    Original,
    Classico,
    Cientifico,
    Minimalista,
    Academico,
    Estatistico,
    Moderno,
    Personalizado,
}

/// <summary>Aparência da tabela. Somente formatação: nenhum valor é tocado.</summary>
public sealed record TableStyle(
    StylePreset Preset,
    string HeaderBackground,
    string HeaderText,
    string ZebraBackground,
    string BodyText,
    string BorderColor,
    string FontName,
    double FontSize,
    bool BoldHeader,
    bool FreezeHeader,
    bool AutoFitColumns)
{
    public static TableStyle For(StylePreset p) => p switch
    {
        StylePreset.Classico => new(p, "#D9D9D9", "#000000", "#FFFFFF", "#000000", "#808080", "Calibri", 11, true, true, true),
        StylePreset.Cientifico => new(p, "#1F3864", "#FFFFFF", "#F2F5FA", "#111111", "#8EA9DB", "Calibri", 10, true, true, true),
        StylePreset.Minimalista => new(p, "#FFFFFF", "#0F172A", "#FFFFFF", "#334155", "#E2E8F0", "Segoe UI", 10, true, true, true),
        StylePreset.Academico => new(p, "#FFFFFF", "#000000", "#FFFFFF", "#000000", "#000000", "Times New Roman", 11, true, true, true),
        StylePreset.Estatistico => new(p, "#E7E6E6", "#000000", "#F8F8F8", "#000000", "#A6A6A6", "Consolas", 10, true, true, true),
        StylePreset.Moderno => new(p, "#047857", "#FFFFFF", "#ECFDF5", "#0F172A", "#A7F3D0", "Segoe UI", 10, true, true, true),
        StylePreset.Personalizado => new(p, "#0F172A", "#FFFFFF", "#F8FAFC", "#0F172A", "#CBD5E1", "Calibri", 11, true, true, true),
        _ => new(StylePreset.Original, "", "", "", "", "", "", 0, false, false, false),
    };

    public string Label => Preset switch
    {
        StylePreset.Original => "Manter aparência original",
        StylePreset.Classico => "1. Clássico",
        StylePreset.Cientifico => "2. Científico",
        StylePreset.Minimalista => "3. Minimalista",
        StylePreset.Academico => "4. Acadêmico",
        StylePreset.Estatistico => "5. Estatístico",
        StylePreset.Moderno => "6. Moderno",
        _ => "7. Personalizado",
    };
}

public sealed record StyleReport(string OutputPath, int CellsChecked, bool ValuesIdentical);

/// <summary>
/// Aplica a aparência numa cópia da planilha preenchida e PROVA que nenhum valor mudou,
/// comparando célula por célula antes e depois.
/// </summary>
public static class XlsxStyler
{
    public static StyleReport Apply(string filledPath, string outputPath, TableStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (File.Exists(outputPath))
        {
            throw new IOException($"Já existe um arquivo em {outputPath}.");
        }

        var before = Snapshot(filledPath);
        File.Copy(filledPath, outputPath);
        File.SetAttributes(outputPath, File.GetAttributes(outputPath) & ~FileAttributes.ReadOnly);
        if (style.Preset != StylePreset.Original)
        {
            var analysis = SpreadsheetAnalyzer.Analyze(filledPath);
            using var wb = new XLWorkbook(outputPath);
            foreach (var info in analysis.Sheets.Where(s => s.ColumnCount > 0))
            {
                var ws = wb.Worksheet(info.Name);
                var h = info.HeaderRow ?? 1;
                var table = ws.Range(h, 1, Math.Max(h, info.RowCount), info.ColumnCount);
                table.Style.Font.FontName = style.FontName;
                table.Style.Font.FontSize = style.FontSize;
                table.Style.Font.FontColor = XLColor.FromHtml(style.BodyText);
                table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                table.Style.Border.OutsideBorderColor = XLColor.FromHtml(style.BorderColor);
                table.Style.Border.InsideBorderColor = XLColor.FromHtml(style.BorderColor);

                var header = ws.Range(h, 1, h, info.ColumnCount);
                header.Style.Fill.BackgroundColor = XLColor.FromHtml(style.HeaderBackground);
                header.Style.Font.FontColor = XLColor.FromHtml(style.HeaderText);
                header.Style.Font.Bold = style.BoldHeader;

                for (var r = h + 1; r <= info.RowCount; r++)
                {
                    if ((r - h) % 2 == 0)
                    {
                        ws.Range(r, 1, r, info.ColumnCount).Style.Fill.BackgroundColor = XLColor.FromHtml(style.ZebraBackground);
                    }
                }

                if (style.FreezeHeader)
                {
                    ws.SheetView.FreezeRows(h);
                }

                if (style.AutoFitColumns)
                {
                    ws.Columns(1, info.ColumnCount).AdjustToContents(h, info.RowCount);
                }
            }

            wb.Save();
        }

        var after = Snapshot(outputPath);
        var identical = before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out var v) && v == kv.Value);
        if (!identical)
        {
            File.Delete(outputPath);
            throw new InvalidOperationException("A formatação alteraria valores; o arquivo não foi gerado.");
        }

        return new StyleReport(outputPath, before.Count, identical);
    }

    /// <summary>Valor bruto + fórmula de cada célula usada (o que importa para os dados).</summary>
    internal static Dictionary<string, string> Snapshot(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ws in wb.Worksheets)
        {
            foreach (var cell in ws.CellsUsed(XLCellsUsedOptions.Contents))
            {
                map[$"{ws.Name}!{cell.Address}"] = cell.HasFormula ? "=" + cell.FormulaA1 : $"{cell.DataType}:{cell.Value}";
            }
        }

        return map;
    }
}
