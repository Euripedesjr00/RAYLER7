using ClosedXML.Excel;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.Infrastructure.Export;

/// <summary>Uma aba já preenchida, como texto exatamente igual ao da planilha (cabeçalho + linhas).</summary>
public sealed record ExportSheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

public static class ExportTable
{
    /// <summary>Lê a planilha preenchida. O cabeçalho é a linha detectada na Etapa 1; títulos acima dele não viram dados.</summary>
    public static IReadOnlyList<ExportSheet> Read(string filledPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filledPath);
        if (!Path.GetExtension(filledPath).StartsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            var (rows, _, _) = CsvFile.Read(filledPath);
            return rows.Count == 0 ? [] : [new ExportSheet("Dados", rows[0], rows.Skip(1).ToList())];
        }

        var analysis = SpreadsheetAnalyzer.Analyze(filledPath);
        using var stream = new FileStream(filledPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        var sheets = new List<ExportSheet>();
        foreach (var info in analysis.Sheets.Where(s => s.ColumnCount > 0))
        {
            var ws = wb.Worksheet(info.Name);
            var header = info.HeaderRow ?? 1;
            var rows = new List<IReadOnlyList<string>>();
            for (var r = header + 1; r <= info.RowCount; r++)
            {
                var row = new List<string>(info.ColumnCount);
                for (var c = 1; c <= info.ColumnCount; c++)
                {
                    row.Add(ws.Cell(r, c).GetFormattedString());
                }

                rows.Add(row);
            }

            sheets.Add(new ExportSheet(info.Name, info.Headers, rows));
        }

        return sheets;
    }
}
