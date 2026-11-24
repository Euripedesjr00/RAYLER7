using System.Globalization;
using System.Text;
using System.Text.Json;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.Infrastructure.Export;

public enum ExportFormat
{
    Xlsx,
    Ods,
    CsvUtf8Semicolon,
    CsvUtf8Comma,
    Tsv,
    Txt,
    Json,
    Dbf,
    PackageR,
    PackageSpss,
    PackageStata,
    PackageSas,
}

/// <summary>Formato com descrição honesta do que é gerado (nativo ou pacote CSV + script de importação).</summary>
public sealed record ExportFormatInfo(ExportFormat Format, string Label, string Extension, bool Native, string Note);

public static class Exporters
{
    public static readonly IReadOnlyList<ExportFormatInfo> Formats =
    [
        new(ExportFormat.Xlsx, "Excel (.xlsx)", ".xlsx", true, "Com a aparência escolhida; fórmulas, mesclagens e abas do modelo preservadas."),
        new(ExportFormat.Ods, "LibreOffice (.ods)", ".ods", true, "Planilha OpenDocument com todas as abas; valores gravados como texto exato."),
        new(ExportFormat.CsvUtf8Semicolon, "CSV UTF-8 (;)", ".csv", true, "Padrão do Excel em português. Todos os valores como texto exato."),
        new(ExportFormat.CsvUtf8Comma, "CSV UTF-8 (,)", ".csv", true, "Padrão internacional. Valores com vírgula decimal ficam entre aspas."),
        new(ExportFormat.Tsv, "TSV (tabulação)", ".tsv", true, "Separado por tabulação; abre em R, Jamovi, JASP e LibreOffice."),
        new(ExportFormat.Txt, "TXT (tabulação)", ".txt", true, "Texto puro separado por tabulação."),
        new(ExportFormat.Json, "JSON", ".json", true, "Lista de registros; todos os valores como texto exato."),
        new(ExportFormat.Dbf, "DBF (dBASE III)", ".dbf", true, "Campos de texto (máx. 254 caracteres; nomes de campo com até 10 letras). Sem perda de zeros ou vírgulas."),
        new(ExportFormat.PackageR, "R / RStudio", ".zip", false, "Pacote: CSV UTF-8 + script .R que importa tudo como texto (sem conversão silenciosa)."),
        new(ExportFormat.PackageSpss, "SPSS", ".zip", false, "Pacote: CSV UTF-8 + sintaxe .sps (GET DATA) com todas as variáveis como texto. Não é um .sav nativo."),
        new(ExportFormat.PackageStata, "Stata", ".zip", false, "Pacote: CSV UTF-8 + .do (import delimited, stringcols(_all)). Não é um .dta nativo."),
        new(ExportFormat.PackageSas, "SAS", ".zip", false, "Pacote: CSV UTF-8 + .sas (DATA step com variáveis de texto)."),
    ];

    public static void Write(ExportFormat format, IReadOnlyList<ExportSheet> sheets, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0)
        {
            throw new InvalidOperationException("Não há dados para exportar.");
        }

        if (File.Exists(outputPath))
        {
            throw new IOException($"Já existe um arquivo em {outputPath}. Escolha outro nome.");
        }

        var sheet = sheets[0];
        switch (format)
        {
            case ExportFormat.CsvUtf8Semicolon:
                CsvFile.Write(outputPath, Rows(sheet), ';');
                break;
            case ExportFormat.CsvUtf8Comma:
                CsvFile.Write(outputPath, Rows(sheet), ',');
                break;
            case ExportFormat.Tsv:
            case ExportFormat.Txt:
                CsvFile.Write(outputPath, Rows(sheet), '\t');
                break;
            case ExportFormat.Ods:
                OdsWriter.Write(outputPath, sheets);
                break;
            case ExportFormat.Json:
                WriteJson(sheets, outputPath);
                break;
            case ExportFormat.Dbf:
                DbfWriter.Write(outputPath, sheet);
                break;
            case ExportFormat.PackageR:
            case ExportFormat.PackageSpss:
            case ExportFormat.PackageStata:
            case ExportFormat.PackageSas:
                ScriptPackage.Write(format, sheet, outputPath);
                break;
            default:
                throw new NotSupportedException("O Excel é gerado pelo XlsxStyler.");
        }
    }

    internal static IEnumerable<IReadOnlyList<string>> Rows(ExportSheet s) => new[] { s.Headers }.Concat(s.Rows);

    private static void WriteJson(IReadOnlyList<ExportSheet> sheets, string path)
    {
        var doc = sheets.ToDictionary(s => s.Name, s => s.Rows.Select(r =>
        {
            var o = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < s.Headers.Count; i++)
            {
                var key = string.IsNullOrWhiteSpace(s.Headers[i]) ? $"Coluna{i + 1}" : s.Headers[i];
                while (o.ContainsKey(key))
                {
                    key += "_";
                }

                o[key] = i < r.Count ? r[i] : string.Empty;
            }

            return o;
        }).ToList());
        File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
    }

    /// <summary>Nomes de variáveis seguros para R/SPSS/Stata/SAS/DBF (sem acento, sem espaço, únicos).</summary>
    public static IReadOnlyList<string> SafeNames(IReadOnlyList<string> headers, int maxLength)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        for (var i = 0; i < headers.Count; i++)
        {
            var folded = Core.Mapping.InstructionParser.Fold(headers[i] ?? string.Empty);
            var sb = new StringBuilder();
            foreach (var ch in folded)
            {
                sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
            }

            var name = sb.ToString().Trim('_');
            while (name.Contains("__", StringComparison.Ordinal))
            {
                name = name.Replace("__", "_", StringComparison.Ordinal);
            }

            if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
            {
                name = "v" + (name.Length == 0 ? (i + 1).ToString(CultureInfo.InvariantCulture) : name);
            }

            name = name.Length > maxLength ? name[..maxLength] : name;
            var candidate = name;
            var n = 2;
            while (!used.Add(candidate))
            {
                var suffix = n.ToString(CultureInfo.InvariantCulture);
                candidate = (name.Length + suffix.Length > maxLength ? name[..(maxLength - suffix.Length)] : name) + suffix;
                n++;
            }

            result.Add(candidate);
        }

        return result;
    }
}
