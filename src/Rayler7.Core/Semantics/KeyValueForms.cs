using System.Text.RegularExpressions;
using Rayler7.Core.Extraction;

namespace Rayler7.Core.Semantics;

/// <summary>
/// Fichas e formulários no formato "Campo: Valor" (ex.: "Nome: João / Placa: ABC-1234 / Peso: 72,5")
/// viram uma tabela: cada rótulo é uma coluna e cada ficha uma linha. Uma ficha nova começa quando um
/// rótulo se repete ou após uma linha em branco. O texto dos valores é mantido exatamente como lido.
/// </summary>
public static partial class KeyValueForms
{
    private sealed record Pair(string Label, List<SourceCell> Parts);

    /// <summary>Retorna a tabela convertida, ou null quando o conteúdo não parece uma ficha.</summary>
    public static SourceTable? TryConvert(SourceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var nonEmpty = table.Rows.Where(r => r.Any(c => c.Text.Trim().Length > 0)).ToList();
        if (nonEmpty.Count < 2)
        {
            return null;
        }

        var parsed = new List<List<Pair>?>(); // null = linha em branco (separador de fichas)
        var kvRows = 0;
        foreach (var row in table.Rows)
        {
            if (!row.Any(c => c.Text.Trim().Length > 0))
            {
                parsed.Add(null);
                continue;
            }

            var pairs = ParseRow(row);
            if (pairs.Count > 0)
            {
                kvRows++;
            }

            parsed.Add(pairs);
        }

        // Precisa ser predominantemente "Campo: Valor" e ter ao menos 2 rótulos diferentes.
        var labels = parsed.Where(p => p is not null).SelectMany(p => p!).Select(p => TextNorm.Token(p.Label)).Distinct().ToList();
        if (kvRows * 10 < nonEmpty.Count * 6 || labels.Count < 2)
        {
            return null;
        }

        var order = new List<string>();
        var display = new Dictionary<string, string>();
        var records = new List<Dictionary<string, SourceCell>>();
        Dictionary<string, SourceCell>? current = null;
        foreach (var line in parsed)
        {
            if (line is null)
            {
                current = null; // linha em branco encerra a ficha
                continue;
            }

            foreach (var pair in line)
            {
                var key = TextNorm.Token(pair.Label);
                if (current is null || current.ContainsKey(key))
                {
                    current = [];
                    records.Add(current);
                }

                if (!display.ContainsKey(key))
                {
                    display[key] = pair.Label.Trim();
                    order.Add(key);
                }

                current[key] = Join(pair.Parts);
            }
        }

        var rows = new List<IReadOnlyList<SourceCell>>
        {
            order.Select(k => new SourceCell(display[k])).ToList(),
        };
        rows.AddRange(records.Where(r => r.Count > 0).Select(r =>
            (IReadOnlyList<SourceCell>)order.Select(k => r.TryGetValue(k, out var v) ? v : new SourceCell(string.Empty)).ToList()));
        var warnings = table.Warnings.Append(
            $"{table.SourceFile}: ficha \"Campo: Valor\" reconhecida — {rows.Count - 1} ficha(s) com {order.Count} campo(s).").ToList();
        return table with { Rows = rows, Warnings = warnings, Label = (table.Label is null ? string.Empty : table.Label + " · ") + "Ficha" };
    }

    private static List<Pair> ParseRow(IReadOnlyList<SourceCell> row)
    {
        var pairs = new List<Pair>();
        Pair? current = null;
        foreach (var cell in row)
        {
            var text = cell.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var m = LabelPattern().Match(text);
            if (m.Success)
            {
                current = new Pair(m.Groups["label"].Value, []);
                pairs.Add(current);
                var rest = m.Groups["value"].Value;
                if (rest.Length > 0)
                {
                    current.Parts.Add(cell with { Text = rest, Characters = null, AlternateText = null });
                }

                continue;
            }

            if (current is null)
            {
                return []; // a linha não começa com "Campo:"; não é linha de ficha
            }

            current.Parts.Add(cell);
        }

        return pairs;
    }

    private static SourceCell Join(List<SourceCell> parts)
    {
        if (parts.Count == 0)
        {
            return new SourceCell(string.Empty);
        }

        if (parts.Count == 1)
        {
            return parts[0];
        }

        var text = string.Join(' ', parts.Select(p => p.Text.Trim()));
        double? conf = parts.All(p => p.Confidence is null) ? null : parts.Min(p => p.Confidence ?? 1);
        var boxes = parts.Select(p => p.Box).OfType<CellBox>().ToList();
        CellBox? box = boxes.Count == 0 ? null : new CellBox(boxes.Min(b => b.X), boxes.Min(b => b.Y),
            boxes.Max(b => b.X + b.Width) - boxes.Min(b => b.X), boxes.Max(b => b.Y + b.Height) - boxes.Min(b => b.Y));
        return new SourceCell(text, conf, null, null, box, parts.Any(p => p.SecondEngineMissed));
    }

    // Rótulo: começa com letra, até 40 caracteres e 6 palavras, termina em ":" (ou "=").
    // "10:30" não é rótulo; "Hora: 10:30" é (rótulo "Hora", valor "10:30").
    [GeneratedRegex(@"^(?<label>\p{L}[\p{L}\p{N} ./()%º°ª#-]{0,39}?)\s*[:=]\s*(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelPattern();
}
