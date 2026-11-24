using Rayler7.Core.Extraction;
using Rayler7.Core.Values;

namespace Rayler7.Core.Semantics;

/// <summary>
/// Encontra a linha de cabeçalho real, pulando títulos ("Ficha de campo 2026"), linhas em branco
/// e metadados acima da tabela. Retorna null quando as linhas parecem só dados (sem cabeçalho).
/// </summary>
public static class HeaderDetector
{
    public const int ScanRows = 12;

    public static int? Detect(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return null;
        }

        var limit = Math.Min(rows.Count, ScanRows);
        var width = rows.Take(Math.Min(rows.Count, 30)).Max(r => r.Count(c => c.Trim().Length > 0));
        int? best = null;
        var bestScore = double.MinValue;
        for (var r = 0; r < limit; r++)
        {
            var cells = rows[r].Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            var text = cells.Count(IsLabel);
            var numbers = cells.Count(IsNumberLike);
            var unique = cells.Select(c => c.ToUpperInvariant()).Distinct().Count();
            var score = (cells.Count * 3) + (text * 2.5) + (unique * 1.5) - (numbers * 4);
            if (cells.Count == 1 && width > 2)
            {
                score -= 14; // título numa célula só
            }

            if (cells.Count < Math.Max(2, width / 2))
            {
                score -= 8; // linha bem mais estreita que a tabela: metadado ("Local: Fazenda X")
            }

            if (r + 1 < rows.Count)
            {
                var next = rows[r + 1].Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
                if (next.Count >= Math.Max(1, cells.Count / 2))
                {
                    score += 2;
                }

                if (next.Count > 0 && next.Count(IsNumberLike) > 0 && numbers == 0)
                {
                    score += 4; // cabeçalho de texto seguido de dados numéricos
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = r;
            }
        }

        if (best is not { } b)
        {
            return null;
        }

        var bestCells = rows[b].Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
        // Um cabeçalho precisa ser majoritariamente texto; se a melhor linha tem números, não há cabeçalho.
        if (bestCells.Count(IsLabel) * 2 < bestCells.Count || bestCells.Any(IsNumberLike))
        {
            return null;
        }

        // Sem nenhuma linha de dado depois dele, não é cabeçalho de tabela.
        return b + 1 < rows.Count ? b : null;
    }

    public static int? Detect(IReadOnlyList<IReadOnlyList<SourceCell>> rows) =>
        Detect(rows.Select(r => (IReadOnlyList<string>)r.Select(c => c.Text).ToList()).ToList());

    private static bool IsLabel(string v) => v.Any(char.IsLetter) && !IsNumberLike(v) &&
        ValueClassifier.Classify(v).Kind is not ValueKind.Date;

    private static bool IsNumberLike(string v) =>
        ValueClassifier.Classify(v).Kind is ValueKind.Integer or ValueKind.Decimal or ValueKind.AmbiguousNumber or ValueKind.Code or ValueKind.Date;
}
