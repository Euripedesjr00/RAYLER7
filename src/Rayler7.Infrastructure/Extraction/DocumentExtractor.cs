using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Rayler7.Core.Extraction;
using Rayler7.Infrastructure.Spreadsheets;
using UglyToad.PdfPig;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Rayler7.Infrastructure.Extraction;

public enum SupportLevel
{
    /// <summary>Implementado e testado.</summary>
    Supported,
    /// <summary>Reconhecido, mas ainda não implementado (ex.: OCR de imagens nesta versão).</summary>
    Pending,
    /// <summary>Formato desconhecido.</summary>
    Unsupported,
}

public sealed record ExtractionOutput(IReadOnlyList<SourceTable> Tables, IReadOnlyList<string> Warnings, int PagesAnalyzed, int ImagesAnalyzed);

/// <summary>
/// Lê arquivos DIGITAIS sem OCR: os textos saem exatamente como estão no arquivo.
/// Matriz de suporte real (o que não está aqui não é anunciado como compatível).
/// </summary>
public static class DocumentExtractor
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm", ".csv", ".tsv", ".txt", ".docx", ".pdf", ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp" };
    private static readonly HashSet<string> PendingOcr = new(StringComparer.OrdinalIgnoreCase) { ".heic" };
    private static readonly HashSet<string> PendingOther = new(StringComparer.OrdinalIgnoreCase) { ".xls", ".ods", ".doc", ".rtf" };

    public static SupportLevel Support(string path)
    {
        var ext = Path.GetExtension(path);
        return Supported.Contains(ext) ? SupportLevel.Supported
            : PendingOcr.Contains(ext) || PendingOther.Contains(ext) ? SupportLevel.Pending
            : SupportLevel.Unsupported;
    }

    public static string PendingReason(string path) =>
        PendingOcr.Contains(Path.GetExtension(path))
            ? "HEIC ainda não é lido. Converta a foto para JPG ou PNG (no celular ou no app Fotos) e envie de novo."
            : $"O formato {Path.GetExtension(path)} ainda não está implementado. Salve como .xlsx/.docx e envie novamente.";

    public static ExtractionOutput Extract(string path, string displayName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(path).ToUpperInvariant();
        return ext switch
        {
            ".XLSX" or ".XLSM" => Excel(path, displayName, ct),
            ".CSV" or ".TSV" or ".TXT" => Delimited(path, displayName),
            ".DOCX" => Word(path, displayName),
            ".PDF" => Pdf(path, displayName, ct),
            ".JPG" or ".JPEG" or ".PNG" or ".BMP" or ".TIF" or ".TIFF" or ".WEBP" => ImageExtractor.Extract(path, displayName, ct),
            _ => throw new NotSupportedException(PendingReason(path)),
        };
    }

    private static ExtractionOutput Excel(string path, string name, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        var tables = new List<SourceTable>();
        var warnings = new List<string>();
        foreach (var ws in wb.Worksheets)
        {
            ct.ThrowIfCancellationRequested();
            var used = ws.RangeUsed();
            if (used is null)
            {
                continue;
            }

            var rows = new List<IReadOnlyList<SourceCell>>();
            for (var r = used.FirstRow().RowNumber(); r <= used.LastRow().RowNumber(); r++)
            {
                var row = new List<SourceCell>();
                for (var c = 1; c <= used.LastColumn().ColumnNumber(); c++)
                {
                    var cell = ws.Cell(r, c);
                    // Texto exibido no Excel (respeita casas decimais e zeros do formato da célula).
                    row.Add(new SourceCell(cell.GetFormattedString()));
                }

                rows.Add(row);
            }

            if (ws.CellsUsed().Any(c => c.HasFormula))
            {
                warnings.Add($"{name} / {ws.Name}: fórmulas foram lidas pelo valor exibido.");
            }

            tables.Add(new SourceTable(name, null, ws.Name, rows, []));
        }

        return new ExtractionOutput(tables, warnings, 0, 0);
    }

    private static ExtractionOutput Delimited(string path, string name)
    {
        var (rows, delimiter, encoding) = CsvFile.Read(path);
        var cells = rows.Select(r => (IReadOnlyList<SourceCell>)r.Select(t => new SourceCell(t)).ToList()).ToList();
        var warn = $"{name}: separador \"{(delimiter == '\t' ? "tab" : delimiter.ToString())}\", codificação {encoding.WebName}.";
        return new ExtractionOutput([new SourceTable(name, null, null, cells, [])], [warn], 0, 0);
    }

    private static ExtractionOutput Word(string path, string name)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        var tables = new List<SourceTable>();
        var warnings = new List<string>();
        if (body is null)
        {
            return new ExtractionOutput(tables, [$"{name}: documento vazio."], 0, 0);
        }

        var index = 0;
        foreach (var t in body.Descendants<W.Table>())
        {
            index++;
            var rows = new List<IReadOnlyList<SourceCell>>();
            foreach (var tr in t.Elements<W.TableRow>())
            {
                var row = new List<SourceCell>();
                foreach (var tc in tr.Elements<W.TableCell>())
                {
                    var sb = new StringBuilder();
                    foreach (var p in tc.Elements<W.Paragraph>())
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append('\n');
                        }

                        sb.Append(p.InnerText);
                    }

                    row.Add(new SourceCell(sb.ToString()));
                }

                rows.Add(row);
            }

            tables.Add(new SourceTable(name, null, $"Tabela {index}", rows, []));
        }

        if (tables.Count == 0)
        {
            warnings.Add($"{name}: o documento não contém tabelas. Texto corrido não é convertido em dados automaticamente.");
        }

        return new ExtractionOutput(tables, warnings, 0, 0);
    }

    /// <summary>
    /// PDF com texto digital: reconstrói linhas pela posição vertical das palavras e células pelos
    /// espaços horizontais. PDFs digitalizados (só imagem) ficam para o OCR.
    /// </summary>
    private static ExtractionOutput Pdf(string path, string name, CancellationToken ct)
    {
        using var pdf = PdfDocument.Open(path);
        var tables = new List<SourceTable>();
        var warnings = new List<string>();
        var pages = 0;
        var scanned = 0;
        foreach (var page in pdf.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pages++;
            var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            if (words.Count == 0)
            {
                scanned++;
                continue;
            }

            var heights = words.Select(w => w.BoundingBox.Height).Where(h => h > 0).OrderBy(h => h).ToList();
            var lineTol = heights.Count > 0 ? heights[heights.Count / 2] * 0.6 : 3;
            var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
            foreach (var w in words.OrderByDescending(w => w.BoundingBox.Centroid.Y).ThenBy(w => w.BoundingBox.Left))
            {
                var line = lines.FirstOrDefault(l => Math.Abs(l[0].BoundingBox.Centroid.Y - w.BoundingBox.Centroid.Y) <= lineTol);
                if (line is null)
                {
                    lines.Add([w]);
                }
                else
                {
                    line.Add(w);
                }
            }

            var rows = new List<IReadOnlyList<SourceCell>>();
            foreach (var line in lines)
            {
                var ordered = line.OrderBy(w => w.BoundingBox.Left).ToList();
                var cells = new List<SourceCell>();
                var current = new StringBuilder(ordered[0].Text);
                for (var i = 1; i < ordered.Count; i++)
                {
                    var gap = ordered[i].BoundingBox.Left - ordered[i - 1].BoundingBox.Right;
                    var h = Math.Max(ordered[i].BoundingBox.Height, 1);
                    if (gap > h * 0.9)
                    {
                        cells.Add(new SourceCell(current.ToString()));
                        current.Clear();
                    }
                    else
                    {
                        current.Append(' ');
                    }

                    current.Append(ordered[i].Text);
                }

                cells.Add(new SourceCell(current.ToString()));
                rows.Add(cells);
            }

            tables.Add(new SourceTable(name, page.Number, $"Página {page.Number}", rows,
                [$"{name} pág. {page.Number}: colunas reconstruídas pelos espaços do PDF — confira o alinhamento."]));
        }

        if (scanned > 0)
        {
            warnings.Add($"{name}: {scanned} página(s) sem texto digital (digitalizadas). Exporte essas páginas como imagem (PNG/JPG) e envie as imagens: elas serão lidas por OCR.");
        }

        return new ExtractionOutput(tables, warnings, pages, 0);
    }
}
