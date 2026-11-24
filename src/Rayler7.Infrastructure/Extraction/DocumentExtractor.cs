using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Rayler7.Core.Extraction;
using Rayler7.Core.Semantics;
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
public static partial class DocumentExtractor
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm", ".xls", ".ods", ".csv", ".tsv", ".txt", ".docx", ".pdf", ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".heic" };
    private static readonly HashSet<string> PendingOcr = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PendingOther = new(StringComparer.OrdinalIgnoreCase) { ".doc", ".rtf" };

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
            : $"O formato {Path.GetExtension(path)} não é lido diretamente. No Word, use Arquivo > Salvar como > Documento do Word (.docx) e envie novamente.";

    public static ExtractionOutput Extract(string path, string displayName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(path).ToUpperInvariant();
        return ext switch
        {
            ".XLSX" or ".XLSM" => Excel(path, displayName, ct),
            ".XLS" => LegacySpreadsheets.Xls(path, displayName, ct),
            ".ODS" => LegacySpreadsheets.Ods(path, displayName, ct),
            ".HEIC" => ImageExtractor.Extract(PageImages.ConvertHeicAsync(path).GetAwaiter().GetResult(), displayName, ct),
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

        // Arquivo de texto com fichas "Campo: Valor": lido linha a linha, sem cortar valores nas vírgulas.
        if (FormText(name, File.ReadAllLines(path, encoding)) is { } form)
        {
            return new ExtractionOutput([form], [$"{name}: lido como ficha \"Campo: Valor\" (codificação {encoding.WebName})."], 0, 0);
        }

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

                        sb.Append(ParagraphText(p));
                    }

                    row.Add(new SourceCell(sb.ToString()));
                }

                rows.Add(row);
            }

            tables.Add(new SourceTable(name, null, $"Tabela {index}", rows, []));
        }

        // Parágrafos fora de tabelas: aproveitados somente se forem fichas "Campo: Valor".
        var lines = body.Elements<W.Paragraph>().Select(ParagraphText).ToList();
        if (FormText(name, lines) is { } form)
        {
            tables.Add(form);
        }
        else if (tables.Count == 0)
        {
            warnings.Add($"{name}: o documento não contém tabelas nem fichas \"Campo: Valor\". Texto corrido não é convertido em dados automaticamente.");
        }

        return new ExtractionOutput(tables, warnings, 0, 0);
    }

    /// <summary>Texto do parágrafo mantendo tabulações e quebras (InnerText as descarta e cola campos vizinhos).</summary>
    private static string ParagraphText(W.Paragraph p)
    {
        var sb = new StringBuilder();
        foreach (var el in p.Descendants())
        {
            switch (el)
            {
                case W.Text t:
                    sb.Append(t.Text);
                    break;
                case W.TabChar:
                    sb.Append('\t');
                    break;
                case W.Break or W.CarriageReturn:
                    sb.Append('\n');
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Linhas de texto como tabela (tabulação ou 3+ espaços separam campos na mesma linha); devolvida
    /// somente quando o conteúdo é uma ficha "Campo: Valor" — texto corrido nunca vira dado.
    /// </summary>
    internal static SourceTable? FormText(string name, IEnumerable<string> lines)
    {
        var rows = lines.Select(l => (IReadOnlyList<SourceCell>)WideGap().Split(l.TrimEnd('\r'))
                .Where(part => part.Length > 0).Select(part => new SourceCell(part.Trim())).ToList())
            .ToList();
        var table = new SourceTable(name, null, "Texto", rows, []);
        return KeyValueForms.TryConvert(table) is null ? null : table;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\t|\s{3,}")]
    private static partial System.Text.RegularExpressions.Regex WideGap();

    /// <summary>
    /// PDF com texto digital: reconstrói linhas pela posição vertical das palavras e células pelos
    /// espaços horizontais. Páginas digitalizadas (só imagem) são lidas por OCR.
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
                OcrPage(path, name, page.Number, tables, warnings, ct);
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
            warnings.Add($"{name}: {scanned} página(s) digitalizada(s) lida(s) por OCR — confira os valores com a imagem da página.");
        }

        return new ExtractionOutput(tables, warnings, pages, scanned);
    }

    /// <summary>Página sem texto digital: o Windows renderiza a página e os dois motores de OCR a leem.</summary>
    private static void OcrPage(string path, string name, int pageNumber, List<SourceTable> tables, List<string> warnings, CancellationToken ct)
    {
        try
        {
            var png = PageImages.RenderPdfPageAsync(path, pageNumber).GetAwaiter().GetResult();
            ct.ThrowIfCancellationRequested();
            var ocr = ImageExtractor.Extract(png, name, ct);
            foreach (var t in ocr.Tables)
            {
                tables.Add(t with { Page = pageNumber, Label = $"Página {pageNumber} (OCR)" });
            }

            warnings.AddRange(ocr.Warnings.Select(w => w.Replace($"{name}:", $"{name} pág. {pageNumber}:", StringComparison.Ordinal)).Distinct());
        }
        catch (InvalidDataException)
        {
            warnings.Add($"{name} pág. {pageNumber}: nenhum texto reconhecido na página digitalizada.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            warnings.Add($"{name} pág. {pageNumber}: não foi possível renderizar a página para OCR ({ex.Message}).");
        }
    }
}
