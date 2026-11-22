using ClosedXML.Excel;

namespace Rayler7.Infrastructure.Spreadsheets;

/// <summary>Lê a estrutura de uma planilha-modelo sem nunca gravar no arquivo original.</summary>
public static class SpreadsheetAnalyzer
{
    public const int PreviewRows = 50;

    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm", ".csv", ".txt", ".tsv" };

    public static SpreadsheetAnalysis Analyze(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var ext = Path.GetExtension(path);
        var hash = FileHash.Sha256(path);
        return ext.ToUpperInvariant() switch
        {
            ".XLSX" or ".XLSM" => AnalyzeExcel(path, hash),
            ".CSV" or ".TXT" or ".TSV" => AnalyzeCsv(path, hash),
            ".XLS" => throw new NotSupportedException("O formato .xls (Excel 97-2003) ainda não está implementado. Salve como .xlsx no Excel ou LibreOffice e tente novamente."),
            ".ODS" => throw new NotSupportedException("O formato .ods ainda não está implementado. Salve como .xlsx no LibreOffice e tente novamente."),
            _ => throw new NotSupportedException($"Formato não suportado: {ext}"),
        };
    }

    private static SpreadsheetAnalysis AnalyzeExcel(string path, string hash)
    {
        var warnings = new List<string>();

        // Abre a partir de um fluxo somente-leitura: o arquivo do usuário nunca é tocado.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        var sheets = new List<SheetAnalysis>();
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed(XLCellsUsedOptions.All);
            if (used is null)
            {
                sheets.Add(new SheetAnalysis(ws.Name, 0, 0, null, [], 0, 0, [], [], false, 0, []));
                continue;
            }

            var lastRow = used.LastRow().RowNumber();
            var lastCol = used.LastColumn().ColumnNumber();
            int filled = 0, empty = 0;
            var formulas = new List<string>();
            int? headerRow = null;
            var headers = new List<string>();

            for (var r = 1; r <= lastRow; r++)
            {
                var rowHasText = false;
                for (var c = 1; c <= lastCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    if (cell.HasFormula)
                    {
                        filled++;
                        formulas.Add($"{cell.Address}: ={cell.FormulaA1}");
                    }
                    else if (cell.IsEmpty())
                    {
                        empty++;
                    }
                    else
                    {
                        filled++;
                        rowHasText = true;
                    }
                }

                if (headerRow is null && rowHasText)
                {
                    headerRow = r;
                }
            }

            if (headerRow is { } hr)
            {
                for (var c = 1; c <= lastCol; c++)
                {
                    headers.Add(ws.Cell(hr, c).GetFormattedString());
                }
            }

            var preview = new List<IReadOnlyList<string>>();
            for (var r = 1; r <= Math.Min(lastRow, PreviewRows); r++)
            {
                var row = new List<string>(lastCol);
                for (var c = 1; c <= lastCol; c++)
                {
                    row.Add(ws.Cell(r, c).GetFormattedString());
                }

                preview.Add(row);
            }

            sheets.Add(new SheetAnalysis(
                ws.Name, lastRow, lastCol, headerRow, headers, filled, empty, formulas,
                ws.MergedRanges.Select(m => m.RangeAddress.ToString() ?? string.Empty).ToList(),
                ws.AutoFilter.IsEnabled,
                ws.ConditionalFormats.Count(),
                preview));
        }

        return new SpreadsheetAnalysis(Path.GetFileName(path), "Excel (.xlsx)", hash, sheets, warnings);
    }

    private static SpreadsheetAnalysis AnalyzeCsv(string path, string hash)
    {
        var (rows, delimiter, encoding) = CsvFile.Read(path);
        var cols = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        int filled = 0, empty = 0;
        foreach (var row in rows)
        {
            for (var c = 0; c < cols; c++)
            {
                if (c < row.Count && row[c].Length > 0)
                {
                    filled++;
                }
                else
                {
                    empty++;
                }
            }
        }

        var headerIdx = rows.Select((r, i) => (r, i)).FirstOrDefault(x => x.r.Any(v => v.Length > 0)).i;
        var headers = rows.Count == 0 ? [] : rows[headerIdx].ToList();
        var warnings = new List<string>
        {
            $"Separador detectado: {(delimiter == '\t' ? "tabulação" : delimiter.ToString())}; codificação: {encoding.WebName}.",
        };
        var sheet = new SheetAnalysis("CSV", rows.Count, cols, rows.Count == 0 ? null : headerIdx + 1, headers,
            filled, empty, [], [], false, 0, rows.Take(PreviewRows).ToList());
        return new SpreadsheetAnalysis(Path.GetFileName(path), "Texto delimitado (CSV)", hash, [sheet], warnings);
    }
}
