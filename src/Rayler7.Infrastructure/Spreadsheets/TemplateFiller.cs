using ClosedXML.Excel;
using Rayler7.Core.Values;

namespace Rayler7.Infrastructure.Spreadsheets;

public enum WriteMode
{
    /// <summary>Padrão: grava o texto exatamente como lido (preserva vírgula, zeros e casas decimais).</summary>
    PreserveText,
    /// <summary>Somente com autorização: grava como número quando a interpretação é segura, com formato de casas decimais.</summary>
    NumberWhenSafe,
}

public sealed record CellWrite(string Sheet, int Row, int Column, string Text);

public sealed record FillConflict(string Sheet, int Row, int Column, string ExistingText, string IncomingText);

public sealed record FillReport(
    string OutputPath,
    int CellsWritten,
    IReadOnlyList<FillConflict> Conflicts,
    IReadOnlyList<string> Warnings,
    string TemplateSha256Before,
    string TemplateSha256After)
{
    public bool TemplateUnchanged => TemplateSha256Before == TemplateSha256After;
}

/// <summary>
/// Preenche uma CÓPIA da planilha-modelo. Nunca sobrescreve o original nem células já preenchidas.
/// </summary>
public static class TemplateFiller
{
    public static FillReport Fill(string templatePath, string outputPath, IEnumerable<CellWrite> writes, WriteMode mode = WriteMode.PreserveText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(writes);
        if (string.Equals(Path.GetFullPath(templatePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O arquivo de saída não pode ser o próprio modelo original.");
        }

        if (File.Exists(outputPath))
        {
            throw new IOException($"Já existe um arquivo em {outputPath}. Escolha outro nome ou confirme a substituição.");
        }

        var before = FileHash.Sha256(templatePath);
        File.Copy(templatePath, outputPath, overwrite: false);
        // A cópia guardada do modelo é somente-leitura; a cópia de trabalho precisa ser gravável.
        File.SetAttributes(outputPath, File.GetAttributes(outputPath) & ~FileAttributes.ReadOnly);

        var conflicts = new List<FillConflict>();
        var warnings = new List<string>();
        var written = 0;
        using (var wb = new XLWorkbook(outputPath))
        {
            foreach (var w in writes)
            {
                if (!wb.TryGetWorksheet(w.Sheet, out var ws))
                {
                    warnings.Add($"Aba \"{w.Sheet}\" não existe no modelo; valor não gravado ({w.Text}).");
                    continue;
                }

                var cell = ws.Cell(w.Row, w.Column);
                if (!cell.IsEmpty() || cell.HasFormula)
                {
                    conflicts.Add(new FillConflict(w.Sheet, w.Row, w.Column, cell.GetFormattedString(), w.Text));
                    continue;
                }

                if (w.Text.Length == 0)
                {
                    continue; // vazio continua vazio
                }

                var value = ValueClassifier.Classify(w.Text);
                if (mode == WriteMode.NumberWhenSafe && value.Interpreted is { } number
                    && value.Kind is ValueKind.Integer or ValueKind.Decimal
                    && value.Original.Trim().Length == value.Original.Length
                    && SignificantDigits(value.Original) <= 15)
                {
                    // O Excel armazena números em dupla precisão (15 dígitos significativos).
                    cell.Value = (double)number;
                    cell.Style.NumberFormat.Format = value.DecimalPlaces > 0
                        ? "0." + new string('0', value.DecimalPlaces)
                        : "0";
                }
                else
                {
                    cell.Value = w.Text;
                    cell.Style.NumberFormat.Format = "@";
                }

                written++;
            }

            wb.Save();
        }

        if (mode == WriteMode.NumberWhenSafe)
        {
            warnings.Add("Valores gravados como número são exibidos com o separador decimal do Windows de quem abrir o arquivo.");
        }

        var after = FileHash.Sha256(templatePath);
        return new FillReport(outputPath, written, conflicts, warnings, before, after);
    }

    private static int SignificantDigits(string s) => s.Count(char.IsAsciiDigit);
}
