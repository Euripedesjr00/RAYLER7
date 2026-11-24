using Rayler7.Core.Extraction;
using Rayler7.Core.Mapping;

namespace Rayler7.Core.Semantics;

/// <summary>
/// Aponta linhas extraídas idênticas a uma linha já existente na planilha-modelo, a um dado já extraído
/// antes, ou a outra linha do mesmo lote. Não apaga nada: a linha vira dúvida e o usuário decide.
/// </summary>
public static class DuplicateDetector
{
    /// <param name="existingRows">Linhas já presentes (modelo e extrações anteriores), indexadas pela coluna 1-based.</param>
    /// <returns>Quantidade de linhas novas marcadas como possivelmente repetidas.</returns>
    public static int Flag(IReadOnlyList<ExtractedRecord> newRecords, IEnumerable<IReadOnlyDictionary<int, string>> existingRows)
    {
        ArgumentNullException.ThrowIfNull(newRecords);
        ArgumentNullException.ThrowIfNull(existingRows);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in existingRows)
        {
            if (Signature(row) is { } sig)
            {
                seen.TryAdd(sig, "já existe na planilha ou em dados extraídos antes");
            }
        }

        var flagged = 0;
        foreach (var group in RowsOf(newRecords))
        {
            var values = group.Where(r => r.TargetColumn is not null)
                .GroupBy(r => ColumnLetters.ToIndex(r.TargetColumn!))
                .ToDictionary(g => g.Key, g => g.First().EffectiveText);
            if (Signature(values) is not { } sig)
            {
                continue;
            }

            if (seen.TryGetValue(sig, out var where))
            {
                var first = group.OrderBy(r => r.TargetColumn is null ? int.MaxValue : ColumnLetters.ToIndex(r.TargetColumn)).First();
                first.AddDoubt(new Doubt(DoubtKind.DuplicateRow,
                    $"Linha repetida: os mesmos valores {where}. Confirme para manter as duas, ou rejeite para descartar a linha inteira.", first.Value.Original, null));
                flagged++;
            }
            else
            {
                seen[sig] = $"aparecem em {group[0].SourceFile}{(group[0].Page is { } p ? $" pág. {p}" : string.Empty)} linha {group[0].Row}";
            }
        }

        return flagged;
    }

    /// <summary>Agrupa os registros de cada linha de origem (mesmo arquivo, página e linha).</summary>
    public static IEnumerable<List<ExtractedRecord>> RowsOf(IEnumerable<ExtractedRecord> records) =>
        records.GroupBy(r => (r.SourceFile, r.Page, r.Row)).Select(g => g.ToList());

    /// <summary>Linha com menos de 2 valores não é comparada (uma célula sozinha repete com facilidade).</summary>
    private static string? Signature(IReadOnlyDictionary<int, string> row)
    {
        var filled = row.Where(kv => kv.Value.Trim().Length > 0).OrderBy(kv => kv.Key).ToList();
        return filled.Count < 2 ? null : string.Join('\u001F', filled.Select(kv => $"{kv.Key}={kv.Value.Trim()}"));
    }
}
