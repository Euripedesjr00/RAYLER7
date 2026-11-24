using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExcelDataReader;
using Rayler7.Core.Extraction;

namespace Rayler7.Infrastructure.Extraction;

/// <summary>Leitura de .xls (Excel 97-2003) e .ods (LibreOffice/OpenOffice) como dados de origem.</summary>
public static class LegacySpreadsheets
{
    private const int MaxColumns = 1024;
    private const int MaxRepeatedRows = 10000;

    static LegacySpreadsheets() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// .xls: cada valor é mostrado com o formato de exibição da própria célula (ex.: 0,00 → "12,50"),
    /// como o Excel mostra. Fórmulas são lidas pelo último valor calculado salvo no arquivo.
    /// </summary>
    public static ExtractionOutput Xls(string path, string name, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateBinaryReader(stream);
        var tables = new List<SourceTable>();
        do
        {
            var rows = new List<List<string>>();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                var row = new List<string>(reader.FieldCount);
                for (var i = 0; i < Math.Min(reader.FieldCount, MaxColumns); i++)
                {
                    row.Add(FormatXls(reader.GetValue(i), reader.GetNumberFormatString(i)));
                }

                rows.Add(row);
            }

            if (Normalize(rows) is { Count: > 0 } cells)
            {
                tables.Add(new SourceTable(name, null, reader.Name, cells, []));
            }
        }
        while (reader.NextResult());

        return new ExtractionOutput(tables,
            [$"{name}: arquivo .xls antigo — valores lidos com o formato de exibição de cada célula; fórmulas pelo último valor salvo."], 0, 0);
    }

    internal static string FormatXls(object? value, string? format)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case string s:
                return s;
            case bool b:
                return b ? "VERDADEIRO" : "FALSO";
        }

        if (!string.IsNullOrEmpty(format) && !format.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            var nf = new ExcelNumberFormat.NumberFormat(format);
            if (nf.IsValid)
            {
                var text = nf.Format(value, CultureInfo.CurrentCulture);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }

        return value switch
        {
            double d => d.ToString("G15", CultureInfo.CurrentCulture),
            DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("d", CultureInfo.CurrentCulture) : dt.ToString("g", CultureInfo.CurrentCulture),
            IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// .ods: usa o texto exibido que o próprio arquivo guarda para cada célula (text:p),
    /// exatamente como aparece na planilha. Nada é recalculado ou reformatado.
    /// </summary>
    public static ExtractionOutput Ods(string path, string name, CancellationToken ct)
    {
        XNamespace t = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content.xml") ?? throw new InvalidDataException($"{name}: arquivo .ods sem conteúdo (content.xml).");
        using var xml = XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var doc = XDocument.Load(xml, LoadOptions.PreserveWhitespace);
        var tables = new List<SourceTable>();
        var formulas = false;
        foreach (var table in doc.Descendants(t + "table"))
        {
            ct.ThrowIfCancellationRequested();
            var rows = new List<List<string>>();
            foreach (var tr in table.Descendants(t + "table-row"))
            {
                var row = new List<string>();
                foreach (var cell in tr.Elements().Where(e => e.Name == t + "table-cell" || e.Name == t + "covered-table-cell"))
                {
                    formulas |= cell.Attribute(t + "formula") is not null;
                    var text = CellText(cell);
                    var repeat = (int?)cell.Attribute(t + "number-columns-repeated") ?? 1;
                    for (var k = 0; k < repeat && row.Count < MaxColumns; k++)
                    {
                        row.Add(text);
                    }
                }

                var rowRepeat = Math.Min((int?)tr.Attribute(t + "number-rows-repeated") ?? 1, MaxRepeatedRows);
                for (var k = 0; k < rowRepeat; k++)
                {
                    rows.Add(row);
                }
            }

            if (Normalize(rows) is { Count: > 0 } cells)
            {
                tables.Add(new SourceTable(name, null, (string?)table.Attribute(t + "name"), cells, []));
            }
        }

        var warnings = new List<string> { $"{name}: valores lidos exatamente como exibidos no arquivo .ods." };
        if (formulas)
        {
            warnings.Add($"{name}: fórmulas foram lidas pelo valor exibido.");
        }

        return new ExtractionOutput(tables, warnings, 0, 0);
    }

    private static string CellText(XElement cell)
    {
        XNamespace x = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        var sb = new StringBuilder();
        foreach (var p in cell.Elements(x + "p"))
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            AppendText(p, sb, x);
        }

        return sb.ToString();
    }

    private static void AppendText(XElement element, StringBuilder sb, XNamespace x)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                sb.Append(text.Value);
            }
            else if (node is XElement e)
            {
                if (e.Name == x + "s")
                {
                    sb.Append(' ', (int?)e.Attribute(x + "c") ?? 1);
                }
                else if (e.Name == x + "tab")
                {
                    sb.Append('\t');
                }
                else if (e.Name == x + "line-break")
                {
                    sb.Append('\n');
                }
                else if (e.Name.Namespace != XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:office:1.0"))
                {
                    AppendText(e, sb, x); // span, a (link) etc.; anotações (office:annotation) ficam de fora
                }
            }
        }
    }

    /// <summary>Remove linhas e colunas vazias do fim e deixa todas as linhas com a mesma largura.</summary>
    private static List<IReadOnlyList<SourceCell>> Normalize(List<List<string>> rows)
    {
        var lastRow = rows.FindLastIndex(r => r.Any(c => c.Length > 0));
        if (lastRow < 0)
        {
            return [];
        }

        var used = rows.Take(lastRow + 1).ToList();
        var width = used.Max(r => r.FindLastIndex(c => c.Length > 0) + 1);
        return used.Select(r => (IReadOnlyList<SourceCell>)Enumerable.Range(0, width)
            .Select(i => new SourceCell(i < r.Count ? r[i] : string.Empty)).ToList()).ToList();
    }
}
