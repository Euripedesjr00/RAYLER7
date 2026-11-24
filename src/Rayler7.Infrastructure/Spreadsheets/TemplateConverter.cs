using ClosedXML.Excel;
using Rayler7.Core.Extraction;
using Rayler7.Core.Semantics;
using Rayler7.Infrastructure.Extraction;

namespace Rayler7.Infrastructure.Spreadsheets;

public sealed record ConvertedTemplate(string XlsxPath, IReadOnlyList<string> Notes);

/// <summary>
/// Permite usar como planilha-modelo um arquivo que não é .xlsx/.csv: .xls, .ods, PDF, Word ou foto.
/// O conteúdo é copiado como TEXTO para uma planilha .xlsx nova (o original não é alterado), com o
/// cabeçalho em negrito. Modelos lidos por OCR precisam ser conferidos pelo usuário antes de confirmar.
/// </summary>
public static class TemplateConverter
{
    public static readonly IReadOnlySet<string> Convertible = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".xls", ".ods", ".pdf", ".docx", ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".heic",
    };

    public static bool CanConvert(string path) => Convertible.Contains(Path.GetExtension(path));

    public static ConvertedTemplate Convert(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToUpperInvariant();
        var notes = new List<string>();

        // OCR e renderização gravam imagens auxiliares ao lado do arquivo: trabalha numa cópia temporária.
        var work = Path.Combine(Path.GetTempPath(), "RAYLER7", "modelo_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        var copy = Path.Combine(work, name);
        File.Copy(path, copy);
        File.SetAttributes(copy, FileAttributes.Normal);

        var output = DocumentExtractor.Extract(copy, name, ct);
        var tables = output.Tables.Where(t => t.Rows.Any(r => r.Any(c => c.Text.Trim().Length > 0))).ToList();
        if (tables.Count == 0)
        {
            throw new InvalidDataException($"{name}: nenhuma tabela foi encontrada para usar como modelo.");
        }

        var isSpreadsheet = ext is ".XLS" or ".ODS";
        if (!isSpreadsheet)
        {
            // Documento/foto: o modelo é a maior tabela encontrada.
            tables = [tables.OrderByDescending(t => t.Rows.Count * t.Rows.Max(r => r.Count)).First()];
            notes.Add($"Modelo criado a partir de \"{name}\" por leitura {(output.ImagesAnalyzed > 0 ? "OCR" : "do documento")}. " +
                      "Confira os nomes das colunas antes de confirmar; se algum estiver errado, corrija no Excel e envie o .xlsx.");
        }
        else
        {
            notes.Add($"Modelo convertido de \"{name}\" para .xlsx. Valores copiados exatamente como exibidos; " +
                      "fórmulas, cores e células mescladas do arquivo original não são copiadas.");
        }

        var xlsx = Path.Combine(work, Path.GetFileNameWithoutExtension(name) + "_modelo.xlsx");
        using (var wb = new XLWorkbook())
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tables)
            {
                var sheetName = SafeSheetName(t.Label is { Length: > 0 } l && isSpreadsheet ? l : "Modelo", used);
                var ws = wb.Worksheets.Add(sheetName);
                var header = HeaderDetector.Detect(t.Rows);
                for (var r = 0; r < t.Rows.Count; r++)
                {
                    for (var c = 0; c < t.Rows[r].Count; c++)
                    {
                        var cell = ws.Cell(r + 1, c + 1);
                        cell.Style.NumberFormat.Format = "@";
                        cell.SetValue(t.Rows[r][c].Text);
                        if (header == r)
                        {
                            cell.Style.Font.Bold = true;
                        }
                    }
                }

                if (!isSpreadsheet && header is { } h && h + 1 < t.Rows.Count)
                {
                    notes.Add($"Aba \"{sheetName}\": {t.Rows.Count - h - 1} linha(s) abaixo do cabeçalho vieram preenchidas no documento e foram mantidas.");
                }

                ws.Columns().AdjustToContents(1, Math.Min(t.Rows.Count, 50));
            }

            wb.SaveAs(xlsx);
        }

        notes.AddRange(output.Warnings.Where(w => w.Contains("preparada", StringComparison.Ordinal) || w.Contains("desfocada", StringComparison.Ordinal)
                                                  || w.Contains("resolução", StringComparison.Ordinal) || w.Contains("reflexo", StringComparison.Ordinal)));
        return new ConvertedTemplate(xlsx, notes);
    }

    private static string SafeSheetName(string name, HashSet<string> used)
    {
        var clean = new string(name.Select(ch => "[]:*?/\\".Contains(ch, StringComparison.Ordinal) ? '_' : ch).ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "Modelo";
        }

        clean = clean.Length > 31 ? clean[..31] : clean;
        var candidate = clean;
        for (var i = 2; !used.Add(candidate); i++)
        {
            var suffix = $" ({i})";
            candidate = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] : clean) + suffix;
        }

        return candidate;
    }
}
