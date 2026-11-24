using Rayler7.Core.Mapping;
using Rayler7.Core.Values;

namespace Rayler7.Core.Semantics;

/// <summary>Como uma coluna da origem foi associada a uma coluna do modelo.</summary>
public enum MatchReason
{
    /// <summary>Mesmo nome de cabeçalho (ou nome contido no outro).</summary>
    Header,
    /// <summary>Nome informado nas instruções do usuário (ex.: "coluna C corresponde a peso").</summary>
    Instruction,
    /// <summary>Sinônimo do mesmo tipo de informação (ex.: "Automóvel" → "Veículo").</summary>
    Synonym,
    /// <summary>Sem nome reconhecido: associada pela mesma posição. Sempre vira dúvida para conferência.</summary>
    Position,
}

/// <param name="SourceColumn">Índice 0-based na origem.</param>
/// <param name="TargetColumn">Índice 1-based no modelo (1 = A).</param>
public sealed record ColumnMatch(int SourceColumn, int TargetColumn, double Score, MatchReason Reason);

public sealed record MatchPlan(IReadOnlyList<ColumnMatch> Matches, bool ByName)
{
    public ColumnMatch? For(int sourceColumn) => Matches.FirstOrDefault(m => m.SourceColumn == sourceColumn);
}

/// <summary>
/// Associa colunas da origem às colunas do modelo pelo significado, sem IA: compara nomes, sinônimos,
/// nomes dados nas instruções e o tipo dos valores de exemplo (um texto não vai para "Peso").
/// Cada coluna do modelo recebe no máximo uma coluna da origem (associação 1 para 1).
/// </summary>
public static class ColumnMatcher
{
    public const double MinScore = 25;

    public static MatchPlan Match(IReadOnlyList<string> sourceHeaders, IReadOnlyList<IReadOnlyList<string>> samplesPerColumn,
        IReadOnlyList<string> templateHeaders, IReadOnlyCollection<ColumnMapping>? mappings)
    {
        ArgumentNullException.ThrowIfNull(sourceHeaders);
        ArgumentNullException.ThrowIfNull(templateHeaders);
        mappings ??= [];
        var targets = Enumerable.Range(1, Math.Max(templateHeaders.Count, mappings.Count == 0 ? 0 : mappings.Max(m => m.ColumnIndex)))
            .Select(i => new Target(i,
                i - 1 < templateHeaders.Count ? templateHeaders[i - 1] : string.Empty,
                mappings.FirstOrDefault(m => m.ColumnIndex == i)))
            .ToList();

        var pairs = new List<(int Src, int Tgt, double Score, MatchReason Reason)>();
        for (var s = 0; s < sourceHeaders.Count; s++)
        {
            var samples = s < samplesPerColumn.Count ? samplesPerColumn[s].Where(v => v.Trim().Length > 0).Take(5).ToList() : [];
            foreach (var t in targets)
            {
                var (score, reason) = Score(sourceHeaders[s], samples, t);
                if (score >= MinScore)
                {
                    pairs.Add((s, t.Index, score, reason));
                }
            }
        }

        // Melhor pontuação primeiro; em empate, prefere a mesma posição.
        var matches = new List<ColumnMatch>();
        foreach (var p in pairs.OrderByDescending(p => p.Score).ThenByDescending(p => p.Src + 1 == p.Tgt).ThenBy(p => p.Tgt))
        {
            if (matches.Any(m => m.SourceColumn == p.Src || m.TargetColumn == p.Tgt))
            {
                continue;
            }

            matches.Add(new ColumnMatch(p.Src, p.Tgt, p.Score, p.Reason));
        }

        var named = sourceHeaders.Count(h => TextNorm.Token(h).Length > 0);
        var byName = matches.Count > 0 && matches.Count * 2 >= Math.Max(1, named);
        return new MatchPlan(matches.OrderBy(m => m.SourceColumn).ToList(), byName);
    }

    /// <summary>Associação pela ordem, usada quando a origem não tem cabeçalho reconhecível.</summary>
    public static MatchPlan ByPosition(int sourceColumns, int templateColumns) =>
        new(Enumerable.Range(0, Math.Min(sourceColumns, templateColumns)).Select(i => new ColumnMatch(i, i + 1, 0, MatchReason.Position)).ToList(), false);

    /// <summary>
    /// Completa um plano por nome com colunas que ficaram sem par: se a coluna do modelo na mesma posição
    /// está livre e os valores são compatíveis, associa pela posição (sempre com dúvida para o usuário conferir).
    /// </summary>
    public static MatchPlan FillByPosition(MatchPlan plan, int sourceColumns, IReadOnlyList<IReadOnlyList<string>> samplesPerColumn,
        IReadOnlyList<string> templateHeaders, IReadOnlyCollection<ColumnMapping>? mappings)
    {
        mappings ??= [];
        var list = plan.Matches.ToList();
        for (var s = 0; s < sourceColumns && s < templateHeaders.Count; s++)
        {
            if (list.Any(m => m.SourceColumn == s) || list.Any(m => m.TargetColumn == s + 1))
            {
                continue;
            }

            var role = SemanticRoles.Infer(templateHeaders[s]);
            var expectsNumber = SemanticRoles.ExpectsNumber(role) || mappings.Any(m => m.ColumnIndex == s + 1 && m.ExpectNumeric);
            var samples = s < samplesPerColumn.Count ? samplesPerColumn[s] : [];
            if (expectsNumber && samples.Any(v => v.Trim().Length > 0 && Kind(v) == ValueKind.Text))
            {
                continue; // texto numa coluna numérica: não chuta
            }

            list.Add(new ColumnMatch(s, s + 1, 0, MatchReason.Position));
        }

        return plan with { Matches = list.OrderBy(m => m.SourceColumn).ToList() };
    }

    private sealed record Target(int Index, string Header, ColumnMapping? Mapping)
    {
        public string Norm { get; } = TextNorm.Token(Header);

        public SemanticRole Role { get; } = SemanticRoles.Infer(Header) is var r && r != SemanticRole.Generic
            ? r
            : SemanticRoles.Infer(Mapping?.Meaning);

        public string? Alias { get; } = Mapping is { Source: not MappingSource.Header } m ? TextNorm.Token(m.Meaning) : null;

        public bool ExpectsNumber => Mapping is { ExpectNumeric: true } || SemanticRoles.ExpectsNumber(Role);
    }

    private static (double Score, MatchReason Reason) Score(string sourceHeader, IReadOnlyList<string> samples, Target t)
    {
        var src = TextNorm.Token(sourceHeader);
        double score = 0;
        var reason = MatchReason.Header;
        double best = 0;
        if (src.Length > 0 && t.Norm.Length > 0)
        {
            best = NameScore(src, t.Norm, 100, 62, 28);
        }

        if (src.Length > 0 && t.Alias is { Length: > 0 } alias)
        {
            var a = NameScore(src, alias, 95, 58, 26);
            if (a > best)
            {
                best = a;
                reason = MatchReason.Instruction;
            }
        }

        if (best == 0 && src.Length > 0 && t.Role != SemanticRole.Generic)
        {
            var words = src.Split(' ');
            var syn = SemanticRoles.Synonyms(t.Role);
            if (words.Any(w => w.Length > 1 && syn.Contains(w)) || SemanticRoles.Infer(sourceHeader) == t.Role)
            {
                best = 48;
                reason = MatchReason.Synonym;
            }
        }

        score += best;
        if (score == 0)
        {
            return (0, reason);
        }

        // Compatibilidade dos valores: a média dos exemplos confirma ou derruba a associação.
        if (samples.Count > 0)
        {
            double adj = 0;
            foreach (var v in samples)
            {
                var k = Kind(v);
                var numeric = k is ValueKind.Integer or ValueKind.Decimal or ValueKind.AmbiguousNumber;
                if (t.ExpectsNumber)
                {
                    adj += numeric ? 15 : k == ValueKind.Text && !IsNotAvailable(v) ? -45 : 0;
                }
                else if (t.Role == SemanticRole.Date)
                {
                    adj += k == ValueKind.Date ? 20 : k == ValueKind.Text ? -35 : 0;
                }
                else if (t.Role == SemanticRole.Name)
                {
                    adj += k == ValueKind.Text ? 12 : numeric ? -35 : 0;
                }
            }

            score += adj / samples.Count;
        }

        return (score, reason);
    }

    private static double NameScore(string src, string target, double exact, double contains, double perWord)
    {
        if (src == target)
        {
            return exact;
        }

        // "altura" ↔ "altura cm": um contém o outro como palavras inteiras.
        if ((" " + target + " ").Contains(" " + src + " ", StringComparison.Ordinal) ||
            (" " + src + " ").Contains(" " + target + " ", StringComparison.Ordinal))
        {
            return contains;
        }

        var srcWords = src.Split(' ').Where(w => w.Length >= 3).ToHashSet();
        var overlap = target.Split(' ').Count(w => w.Length >= 3 && srcWords.Contains(w));
        return overlap * perWord;
    }

    private static ValueKind Kind(string v) => ValueClassifier.Classify(v.Trim()).Kind;

    private static bool IsNotAvailable(string v) => TextNorm.Token(v) is "n a" or "na" or "nd" or "s n" or "sn" or "ilegivel";
}
