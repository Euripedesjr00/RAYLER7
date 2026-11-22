namespace Rayler7.Core.Mapping;

public enum MappingSource
{
    Header,
    Instruction,
    Manual,
}

/// <summary>Associação entre uma coluna da planilha-modelo e a informação esperada.</summary>
public sealed record ColumnMapping(
    int ColumnIndex,
    string? Header,
    string Meaning,
    bool ExpectNumeric,
    IReadOnlyDictionary<string, string> ValueDictionary,
    MappingSource Source)
{
    /// <summary>Letra da coluna no padrão Excel (1 = A, 27 = AA).</summary>
    public string ColumnLetter => ColumnLetters.FromIndex(ColumnIndex);
}

public static class ColumnLetters
{
    public static string FromIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(index);
        var s = string.Empty;
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            s = (char)('A' + rem) + s;
            index = (index - 1) / 26;
        }

        return s;
    }

    public static int ToIndex(string letters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(letters);
        var n = 0;
        foreach (var ch in letters.Trim().ToUpperInvariant())
        {
            if (ch is < 'A' or > 'Z')
            {
                throw new FormatException($"Coluna inválida: {letters}");
            }

            n = (n * 26) + (ch - 'A' + 1);
        }

        return n;
    }
}
