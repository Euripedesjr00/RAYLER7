using System.Globalization;
using System.Text.RegularExpressions;

namespace Rayler7.Core.Values;

/// <summary>
/// Classifica textos sem nunca alterá-los. Toda normalização acontece numa cópia local
/// usada apenas para obter <see cref="PreservedValue.Interpreted"/>.
/// </summary>
public static partial class ValueClassifier
{
    public const string IllegibleMarker = "\u0000ILEGIVEL\u0000";

    public static PreservedValue Classify(string? original, DecimalSeparator columnHint = DecimalSeparator.None)
    {
        var warnings = new List<string>();
        if (original is null || original.Length == 0)
        {
            return new PreservedValue(original, ValueKind.Empty, DecimalSeparator.None, 0, null, null, warnings);
        }

        if (original == IllegibleMarker)
        {
            return new PreservedValue(original, ValueKind.Illegible, DecimalSeparator.None, 0, null, null, warnings);
        }

        var t = original.Trim();
        if (t.Length == 0)
        {
            warnings.Add("A célula contém apenas espaços; o conteúdo original foi mantido.");
            return new PreservedValue(original, ValueKind.Empty, DecimalSeparator.None, 0, null, null, warnings);
        }

        if (t.Length != original.Length)
        {
            warnings.Add("Há espaços antes ou depois do valor; o original foi mantido sem cortes.");
        }

        if (LeadingZeroCode().IsMatch(t))
        {
            warnings.Add("Zeros à esquerda: tratado como código para não perder os zeros.");
            return new PreservedValue(original, ValueKind.Code, DecimalSeparator.None, 0, null, null, warnings);
        }

        if (TryDate(t, out var date))
        {
            return new PreservedValue(original, ValueKind.Date, DecimalSeparator.None, 0, null, date, warnings);
        }

        if (IntegerPattern().IsMatch(t))
        {
            return Number(original, t, ValueKind.Integer, DecimalSeparator.None, 0, t, warnings);
        }

        // Milhar inequívoco: dois ou mais grupos, ou grupos + parte decimal.
        if (PtThousandsWithDecimals().IsMatch(t))
        {
            var places = t.Length - t.LastIndexOf(',') - 1;
            return Number(original, t, ValueKind.Decimal, DecimalSeparator.Comma, places,
                t.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.'), warnings);
        }

        if (EnThousandsWithDecimals().IsMatch(t))
        {
            var places = t.Length - t.LastIndexOf('.') - 1;
            return Number(original, t, ValueKind.Decimal, DecimalSeparator.Dot, places,
                t.Replace(",", string.Empty, StringComparison.Ordinal), warnings);
        }

        if (PtThousandsMultiGroup().IsMatch(t))
        {
            return Number(original, t, ValueKind.Integer, DecimalSeparator.None, 0,
                t.Replace(".", string.Empty, StringComparison.Ordinal), warnings);
        }

        if (EnThousandsMultiGroup().IsMatch(t))
        {
            return Number(original, t, ValueKind.Integer, DecimalSeparator.None, 0,
                t.Replace(",", string.Empty, StringComparison.Ordinal), warnings);
        }

        var single = SingleSeparator().Match(t);
        if (single.Success)
        {
            var sep = single.Groups["sep"].Value[0];
            var intPart = single.Groups["int"].Value;
            var fracPart = single.Groups["frac"].Value;
            var separator = sep == ',' ? DecimalSeparator.Comma : DecimalSeparator.Dot;
            var normalized = t.Replace(',', '.');

            // "1.234" ou "1,234": pode ser milhar ou decimal. Sem dica da coluna, não decidimos.
            var digitsBefore = intPart.TrimStart('+', '-').Length;
            var couldBeThousands = fracPart.Length == 3 && digitsBefore is >= 1 and <= 3 && intPart.TrimStart('+', '-') != "0";
            if (couldBeThousands && columnHint != separator)
            {
                warnings.Add($"\"{t}\" pode ser decimal ou separador de milhar. Confirme o significado.");
                return new PreservedValue(original, ValueKind.AmbiguousNumber, DecimalSeparator.Ambiguous,
                    fracPart.Length, null, null, warnings);
            }

            return Number(original, t, ValueKind.Decimal, separator, fracPart.Length, normalized, warnings);
        }

        return new PreservedValue(original, ValueKind.Text, DecimalSeparator.None, 0, null, null, warnings);
    }

    private static PreservedValue Number(string original, string trimmed, ValueKind kind, DecimalSeparator sep,
        int places, string invariant, List<string> warnings)
    {
        _ = trimmed;
        if (decimal.TryParse(invariant, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
        {
            return new PreservedValue(original, kind, sep, places, value, null, warnings);
        }

        warnings.Add("Número fora do intervalo de cálculo seguro; mantido apenas como texto original.");
        return new PreservedValue(original, kind, sep, places, null, null, warnings);
    }

    private static bool TryDate(string t, out DateOnly date)
    {
        date = default;
        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd"];
        if (!DateShape().IsMatch(t))
        {
            return false;
        }

        return DateOnly.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    [GeneratedRegex(@"^[+-]?0\d+$")]
    private static partial Regex LeadingZeroCode();

    [GeneratedRegex(@"^[+-]?\d+$")]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"^[+-]?\d{1,3}(\.\d{3})+,\d+$")]
    private static partial Regex PtThousandsWithDecimals();

    [GeneratedRegex(@"^[+-]?\d{1,3}(,\d{3})+\.\d+$")]
    private static partial Regex EnThousandsWithDecimals();

    [GeneratedRegex(@"^[+-]?\d{1,3}(\.\d{3}){2,}$")]
    private static partial Regex PtThousandsMultiGroup();

    [GeneratedRegex(@"^[+-]?\d{1,3}(,\d{3}){2,}$")]
    private static partial Regex EnThousandsMultiGroup();

    [GeneratedRegex(@"^(?<int>[+-]?\d+)(?<sep>[.,])(?<frac>\d+)$")]
    private static partial Regex SingleSeparator();

    [GeneratedRegex(@"^(\d{1,2}/\d{1,2}/\d{2}(\d{2})?|\d{4}-\d{2}-\d{2})$")]
    private static partial Regex DateShape();
}
