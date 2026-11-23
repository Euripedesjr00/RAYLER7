using System.Text;
using Rayler7.Core.Extraction;

namespace Rayler7.Infrastructure.Extraction;

/// <summary>
/// Reconstrói linhas e células a partir de palavras posicionadas (OCR de imagem ou PDF):
/// palavras na mesma altura formam uma linha; espaços largos separam as células.
/// </summary>
public static class LayoutTable
{
    public static List<IReadOnlyList<SourceCell>> Build(IReadOnlyList<OcrWord> words, Func<OcrWord, string?>? alternate = null)
    {
        ArgumentNullException.ThrowIfNull(words);
        var rows = new List<IReadOnlyList<SourceCell>>();
        if (words.Count == 0)
        {
            return rows;
        }

        var heights = words.Select(w => w.Height).Where(h => h > 0).OrderBy(h => h).ToList();
        var median = heights.Count > 0 ? heights[heights.Count / 2] : 10;
        var lines = new List<List<OcrWord>>();
        foreach (var w in words.OrderBy(w => w.CenterY).ThenBy(w => w.X))
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l.Average(x => x.CenterY) - w.CenterY) <= median * 0.55);
            if (line is null)
            {
                lines.Add([w]);
            }
            else
            {
                line.Add(w);
            }
        }

        foreach (var line in lines.OrderBy(l => l.Average(w => w.CenterY)))
        {
            var ordered = line.OrderBy(w => w.X).ToList();
            var cells = new List<SourceCell>();
            var group = new List<OcrWord> { ordered[0] };
            for (var i = 1; i < ordered.Count; i++)
            {
                var gap = ordered[i].X - ordered[i - 1].Right;
                if (gap > Math.Max(ordered[i].Height, median) * 0.9)
                {
                    cells.Add(ToCell(group, alternate));
                    group = [];
                }

                group.Add(ordered[i]);
            }

            cells.Add(ToCell(group, alternate));
            rows.Add(cells);
        }

        return rows;
    }

    private static SourceCell ToCell(List<OcrWord> group, Func<OcrWord, string?>? alternate)
    {
        var text = string.Join(' ', group.Select(w => w.Text));
        double? confidence = group.All(w => w.Confidence is null) ? null : group.Min(w => w.Confidence ?? 1);
        List<CharConfidence>? chars = null;
        if (group.All(w => w.Characters is not null))
        {
            chars = [];
            for (var i = 0; i < group.Count; i++)
            {
                if (i > 0)
                {
                    chars.Add(new CharConfidence(' ', 1));
                }

                chars.AddRange(group[i].Characters!);
            }

            if (chars.Count != text.Length)
            {
                chars = null; // alinhamento inseguro: melhor não apontar caractere errado
            }
        }

        string? alt = null;
        var missed = false;
        if (alternate is not null)
        {
            var parts = group.Select(alternate).ToList();
            missed = parts.Any(p => p is null);
            alt = missed ? null : string.Join(' ', parts);
        }

        var x = group.Min(w => w.X);
        var y = group.Min(w => w.Y);
        var box = new CellBox(x, y, group.Max(w => w.Right) - x, group.Max(w => w.Y + w.Height) - y);
        return new SourceCell(text, confidence, alt, chars, box, missed);
    }

    /// <summary>
    /// Texto do outro motor na mesma posição. Usa a sobreposição relativa à menor caixa, porque os
    /// motores desenham caixas de tamanhos diferentes (principalmente em palavras curtas como "T1").
    /// </summary>
    public static Func<OcrWord, string?> MatchFrom(IReadOnlyList<OcrWord> other) => w =>
    {
        var hits = other
            .Select(o =>
            {
                var ix = Math.Max(0, Math.Min(w.Right, o.Right) - Math.Max(w.X, o.X));
                var iy = Math.Max(0, Math.Min(w.Y + w.Height, o.Y + o.Height) - Math.Max(w.Y, o.Y));
                var smaller = Math.Min(w.Width * w.Height, o.Width * o.Height);
                return (Word: o, Ratio: smaller > 0 ? ix * iy / smaller : 0);
            })
            .Where(h => h.Ratio >= 0.5)
            .OrderBy(h => h.Word.X)
            .ToList();
        return hits.Count == 0 ? null : string.Join(' ', hits.Select(h => h.Word.Text));
    };
}
