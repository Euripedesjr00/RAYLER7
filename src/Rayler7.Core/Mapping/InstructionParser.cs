using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rayler7.Core.Mapping;

public sealed record MappingProposal(int ColumnIndex, string? Meaning, IReadOnlyDictionary<string, string> ValueDictionary, string SourceSentence);

public sealed record InstructionParseResult(
    IReadOnlyList<MappingProposal> Proposals,
    IReadOnlyList<string> RecognizedRules,
    IReadOnlyList<string> Unrecognized,
    IReadOnlyList<string> Ambiguities);

/// <summary>
/// Interpretador de instruções em português, sem IA: reconhece padrões comuns
/// ("coluna A corresponde a…", "na primeira coluna, 1 significa carro…").
/// O que não reconhece é devolvido ao usuário — nunca é adivinhado.
/// </summary>
public static partial class InstructionParser
{
    private static readonly string[] Ordinals =
    [
        "primeira", "segunda", "terceira", "quarta", "quinta",
        "sexta", "setima", "oitava", "nona", "decima",
    ];

    private static readonly (string Pattern, string Rule)[] PreservationRules =
    [
        ("nao arredond", "Não arredondar valores"),
        ("nao modific", "Não modificar valores"),
        ("nao alter", "Não alterar valores"),
        ("nao convert", "Não converter valores ou unidades"),
        ("nao realiz", "Não realizar cálculos"),
        ("nao calcul", "Não realizar cálculos"),
        ("nao substitu", "Não substituir valores ausentes"),
        ("nao preench", "Não preencher células sem dado correspondente"),
        ("nao interpret", "Não reinterpretar tipos de dado"),
        ("casas decimais", "Preservar casas decimais"),
        ("mesma ordem", "Manter a ordem da planilha original"),
    ];

    public static InstructionParseResult Parse(string? text, int knownColumnCount = 0)
    {
        var proposals = new List<MappingProposal>();
        var rules = new List<string>();
        var unrecognized = new List<string>();
        var ambiguities = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new InstructionParseResult(proposals, rules, unrecognized, ambiguities);
        }

        foreach (var raw in SplitSentences(text))
        {
            var sentence = raw.Trim().TrimEnd('.', ';');
            if (sentence.Length == 0)
            {
                continue;
            }

            var norm = Fold(sentence);
            var matchedRule = false;
            foreach (var (pattern, rule) in PreservationRules)
            {
                if (norm.Contains(pattern, StringComparison.Ordinal))
                {
                    if (!rules.Contains(rule))
                    {
                        rules.Add(rule);
                    }

                    matchedRule = true;
                }
            }

            if (matchedRule)
            {
                continue;
            }

            var column = FindColumn(sentence, norm);
            if (column is null)
            {
                unrecognized.Add(sentence);
                continue;
            }

            var (index, afterColumn) = column.Value;
            if (knownColumnCount > 0 && index > knownColumnCount)
            {
                ambiguities.Add($"\"{sentence}\": a planilha-modelo tem só {knownColumnCount} coluna(s).");
                continue;
            }

            var dict = ParseDictionary(sentence, norm, afterColumn);
            string? meaning = null;
            if (dict.Count == 0)
            {
                meaning = ExtractMeaning(sentence, norm, afterColumn);
                if (string.IsNullOrWhiteSpace(meaning))
                {
                    unrecognized.Add(sentence);
                    continue;
                }
            }

            proposals.Add(new MappingProposal(index, meaning, dict, sentence));
        }

        // Duas definições diferentes para a mesma coluna: não escolhemos, perguntamos.
        foreach (var group in proposals.Where(p => p.Meaning is not null).GroupBy(p => p.ColumnIndex))
        {
            var meanings = group.Select(p => p.Meaning!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (meanings.Count > 1)
            {
                ambiguities.Add($"Coluna {ColumnLetters.FromIndex(group.Key)} recebeu definições diferentes: {string.Join(" / ", meanings)}.");
            }
        }

        return new InstructionParseResult(proposals, rules, unrecognized, ambiguities);
    }

    private static IEnumerable<string> SplitSentences(string text) =>
        SentenceSplit().Split(text.Replace("\r", string.Empty, StringComparison.Ordinal));

    /// <summary>Remove acentos preservando o comprimento, e põe em minúsculas.</summary>
    internal static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            var d = ch.ToString().Normalize(NormalizationForm.FormD);
            var baseChar = d.FirstOrDefault(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
            sb.Append(char.ToLowerInvariant(baseChar == default ? ch : baseChar));
        }

        return sb.ToString();
    }

    private static (int Index, int End)? FindColumn(string sentence, string norm)
    {
        foreach (Match m in ColumnRef().Matches(norm))
        {
            var token = m.Groups["ref"].Value;
            var originalToken = sentence.Substring(m.Groups["ref"].Index, token.Length);
            var isNumber = char.IsAsciiDigit(token[0]);

            // Letras só valem como referência de coluna se escritas em maiúsculas ("coluna B"),
            // para não confundir com palavras ("coluna de peso").
            if (!isNumber && !originalToken.All(char.IsAsciiLetterUpper))
            {
                continue;
            }

            var idx = int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? n
                : ColumnLetters.ToIndex(token);
            if (idx > 0)
            {
                return (idx, m.Index + m.Length);
            }
        }

        var o = OrdinalRef().Match(norm);
        if (o.Success)
        {
            var pos = Array.IndexOf(Ordinals, o.Groups["ord"].Value);
            return pos >= 0 ? (pos + 1, o.Index + o.Length) : null;
        }

        return null;
    }

    private static Dictionary<string, string> ParseDictionary(string sentence, string norm, int start)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in DictionaryEntry().Matches(norm, start))
        {
            var key = sentence.Substring(m.Groups["key"].Index, m.Groups["key"].Length);
            var val = sentence.Substring(m.Groups["val"].Index, m.Groups["val"].Length).Trim();
            if (val.Length > 0)
            {
                dict[key] = val;
            }
        }

        return dict;
    }

    private static string? ExtractMeaning(string sentence, string norm, int start)
    {
        var m = MeaningLead().Match(norm, start);
        var from = m.Success ? m.Index + m.Length : start;
        if (from >= sentence.Length)
        {
            return null;
        }

        var meaning = sentence[from..].Trim().Trim(',', ':', '-', '=', ' ');
        return meaning.Length == 0 ? null : meaning;
    }

    [GeneratedRegex(@"(?:\r?\n|;|\.\s+|\.$)")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"\bcoluna\s+(?<ref>[a-z]{1,3}|\d{1,3})\b")]
    private static partial Regex ColumnRef();

    [GeneratedRegex(@"\b(?<ord>primeira|segunda|terceira|quarta|quinta|sexta|setima|oitava|nona|decima)\s+coluna\b")]
    private static partial Regex OrdinalRef();

    [GeneratedRegex(@"(?:o\s+)?(?:numero|valor|codigo)?\s*(?<key>[\w.,-]+)\s+(?:significa|=|representa|indica)\s+(?<val>[^,;]+?)(?=\s+e\s+(?:o\s+)?(?:numero|valor|codigo)\b|,|;|$)")]
    private static partial Regex DictionaryEntry();

    [GeneratedRegex(@"\G[\s,:=\-]*(?:(?:corresponde\s+(?:aos|as|ao|a)|representa|sera|recebe|contem|devera\s+conter|deve\s+conter|inserir|colocar|preencher\s+com|indica|e)\s+)?(?:(?:o|a|os|as)\s+)?")]
    private static partial Regex MeaningLead();
}
