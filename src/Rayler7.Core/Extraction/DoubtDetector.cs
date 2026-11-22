using Rayler7.Core.Values;

namespace Rayler7.Core.Extraction;

/// <summary>
/// Regras determinísticas que apontam dúvidas. Apenas sinalizam e sugerem — nunca corrigem.
/// </summary>
public sealed class DoubtDetector
{
    private static readonly Dictionary<char, char> Confusable = new()
    {
        ['0'] = '8', ['8'] = '0',
        ['1'] = '7', ['7'] = '1',
        [','] = '.', ['.'] = ',',
    };

    private static readonly Dictionary<char, char> LetterLooksLikeDigit = new()
    {
        ['O'] = '0', ['o'] = '0', ['D'] = '0',
        ['l'] = '1', ['I'] = '1', ['|'] = '1',
        ['S'] = '5', ['B'] = '8', ['Z'] = '2',
    };

    public double LowConfidenceThreshold { get; init; } = 0.80;

    public double AmbiguousCharThreshold { get; init; } = 0.92;

    public IReadOnlyList<Doubt> Inspect(OcrReading reading, ColumnExpectation? expectation = null)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var doubts = new List<Doubt>();
        var text = reading.Text;

        if (text.Length == 0 || text == ValueClassifier.IllegibleMarker)
        {
            doubts.Add(new Doubt(DoubtKind.Illegible, "Não foi possível ler este campo.", text, null));
            return doubts;
        }

        if (reading.WordConfidence is { } wc && wc < LowConfidenceThreshold)
        {
            doubts.Add(new Doubt(DoubtKind.LowConfidence,
                $"Leitura com confiança baixa ({wc:P0}).", text, null));
        }

        if (reading.Characters is { Count: > 0 } chars)
        {
            for (var i = 0; i < chars.Count; i++)
            {
                var c = chars[i];
                if (c.Confidence < AmbiguousCharThreshold && Confusable.TryGetValue(c.Character, out var alt))
                {
                    var suggestion = ReplaceAt(text, i, alt);
                    doubts.Add(new Doubt(DoubtKind.AmbiguousCharacter,
                        $"O caractere '{c.Character}' (posição {i + 1}) pode ser '{alt}'.", text, suggestion));
                }
            }
        }

        if (reading.AlternateText is { } other && !string.Equals(other.Trim(), text.Trim(), StringComparison.Ordinal))
        {
            doubts.Add(new Doubt(DoubtKind.EngineDisagreement,
                $"Os dois motores de leitura discordam: \"{text}\" × \"{other}\".", text, other));
        }

        var value = ValueClassifier.Classify(text);
        if (value.Kind == ValueKind.AmbiguousNumber)
        {
            doubts.Add(new Doubt(DoubtKind.AmbiguousSeparator,
                "Não está claro se o separador é decimal ou de milhar.", text, null));
        }

        if (expectation is { Numeric: true })
        {
            if (value.Kind is ValueKind.Text)
            {
                var suggestion = new string(text.Select(ch => LetterLooksLikeDigit.TryGetValue(ch, out var d) ? d : ch).ToArray());
                doubts.Add(new Doubt(DoubtKind.TypeMismatch,
                    "A coluna espera número, mas a leitura contém letras.", text,
                    suggestion != text ? suggestion : null));
            }
            else if (expectation.TypicalDecimalPlaces is { } places && value.Kind == ValueKind.Decimal
                     && value.DecimalPlaces != places)
            {
                doubts.Add(new Doubt(DoubtKind.DecimalPlacesMismatch,
                    $"A coluna costuma ter {places} casa(s) decimal(is); este valor tem {value.DecimalPlaces}.", text, null));
            }
        }

        return doubts;
    }

    private static string ReplaceAt(string s, int index, char c)
    {
        if (index >= s.Length)
        {
            return s;
        }

        var arr = s.ToCharArray();
        arr[index] = c;
        return new string(arr);
    }
}
