namespace Rayler7.Core.Values;

/// <summary>
/// Valor com três camadas separadas (regra central do RAYLER7):
/// 1) <see cref="Original"/>: o texto exatamente como foi lido — nunca é modificado;
/// 2) <see cref="Interpreted"/>: o número usado apenas para processamento (pode ser nulo);
/// 3) a correção, quando existir, vive fora deste objeto e exige autorização registrada.
/// </summary>
public sealed record PreservedValue
{
    public PreservedValue(string? original, ValueKind kind, DecimalSeparator separator, int decimalPlaces,
        decimal? interpreted, DateOnly? interpretedDate, IReadOnlyList<string> warnings)
    {
        Original = original ?? string.Empty;
        Kind = kind;
        Separator = separator;
        DecimalPlaces = decimalPlaces;
        Interpreted = interpreted;
        InterpretedDate = interpretedDate;
        Warnings = warnings;
    }

    /// <summary>Texto original, byte a byte como foi recebido.</summary>
    public string Original { get; }

    public ValueKind Kind { get; }

    public DecimalSeparator Separator { get; }

    /// <summary>Quantidade de casas decimais escritas no original (ex.: "10,00" = 2).</summary>
    public int DecimalPlaces { get; }

    /// <summary>Valor numérico apenas para cálculo. Nulo quando não há interpretação segura.</summary>
    public decimal? Interpreted { get; }

    public DateOnly? InterpretedDate { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool IsEmpty => Kind == ValueKind.Empty;

    public bool HasSafeNumericInterpretation => Interpreted.HasValue;

    public override string ToString() => Original;
}
