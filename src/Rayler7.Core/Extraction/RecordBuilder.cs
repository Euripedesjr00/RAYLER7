using Rayler7.Core.Mapping;
using Rayler7.Core.Values;

namespace Rayler7.Core.Extraction;

public sealed record BuildResult(IReadOnlyList<ExtractedRecord> Records, IReadOnlyList<string> Warnings, bool HeaderRowSkipped, bool MatchedByHeader);

/// <summary>
/// Transforma tabelas lidas em registros revisáveis, associando cada coluna de origem a uma coluna
/// do modelo: pelo nome do cabeçalho quando os nomes conferem; caso contrário, na mesma ordem.
/// Nunca cria colunas novas: colunas excedentes viram registros "sem destino" para o usuário decidir.
/// </summary>
public static class RecordBuilder
{
    public static BuildResult Build(SourceTable table, IReadOnlyList<string> templateHeaders, IReadOnlyCollection<ColumnMapping> mappings, DoubtDetector? detector = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(templateHeaders);
        ArgumentNullException.ThrowIfNull(mappings);
        detector ??= new DoubtDetector();
        var warnings = new List<string>(table.Warnings);
        var records = new List<ExtractedRecord>();
        if (table.Rows.Count == 0)
        {
            warnings.Add($"{table.SourceFile}: nenhuma linha encontrada.");
            return new BuildResult(records, warnings, false, false);
        }

        var first = table.Rows[0];
        var headerIndex = MatchHeaders(first, templateHeaders);
        var matchedByHeader = headerIndex is not null;
        var skipHeader = matchedByHeader || LooksLikeHeader(table.Rows);
        var templateCols = Math.Max(templateHeaders.Count, mappings.Count == 0 ? 0 : mappings.Max(m => m.ColumnIndex));
        var modeWidth = table.Rows.Skip(skipHeader ? 1 : 0).GroupBy(r => r.Count).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

        for (var r = skipHeader ? 1 : 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            if (row.All(c => c.Text.Length == 0))
            {
                continue; // linha totalmente vazia na origem: não vira registro
            }

            for (var c = 0; c < row.Count; c++)
            {
                var cell = row[c];
                int? target = matchedByHeader
                    ? (headerIndex!.TryGetValue(c, out var t) ? t : null)
                    : c + 1 <= templateCols ? c + 1 : null;
                var mapping = target is { } ti ? mappings.FirstOrDefault(m => m.ColumnIndex == ti) : null;
                var expectation = mapping is { ExpectNumeric: true } ? new ColumnExpectation(true, null) : null;
                var doubts = cell.Text.Length == 0 && cell.Confidence is null
                    ? new List<Doubt>()
                    : detector.Inspect(new OcrReading(cell.Text, cell.Characters, cell.Confidence, cell.AlternateText), expectation).ToList();
                if (target is null)
                {
                    doubts.Add(new Doubt(DoubtKind.NoTargetColumn,
                        "Esta coluna da origem não tem coluna correspondente no modelo. Nenhuma coluna nova é criada sem sua decisão.", cell.Text, null));
                }

                if (c == 0 && modeWidth > 0 && row.Count != modeWidth)
                {
                    doubts.Add(new Doubt(DoubtKind.RowShape,
                        $"Esta linha tem {row.Count} coluna(s); a maioria das linhas tem {modeWidth}. Confira o alinhamento.", cell.Text, null));
                }

                var field = mapping?.Meaning ?? (target is { } tt && tt - 1 < templateHeaders.Count ? templateHeaders[tt - 1] : $"Coluna {c + 1} da origem");
                records.Add(new ExtractedRecord(Guid.NewGuid(), table.SourceFile, table.Page, r + 1, ColumnLetters.FromIndex(c + 1), field,
                    ValueClassifier.Classify(cell.Text), cell.Confidence, doubts, target is { } x ? ColumnLetters.FromIndex(x) : null));
            }
        }

        if (!matchedByHeader)
        {
            warnings.Add($"{table.SourceFile}{(table.Page is { } p ? $" (pág. {p})" : string.Empty)}: colunas associadas pela ordem ao modelo"
                         + (skipHeader ? "; primeira linha tratada como cabeçalho." : "."));
        }

        return new BuildResult(records, warnings, skipHeader, matchedByHeader);
    }

    /// <summary>Se ao menos metade dos cabeçalhos da origem conferem com o modelo, associa por nome.</summary>
    private static Dictionary<int, int>? MatchHeaders(IReadOnlyList<SourceCell> first, IReadOnlyList<string> templateHeaders)
    {
        var normalizedTemplate = templateHeaders.Select(Norm).ToList();
        var map = new Dictionary<int, int>();
        for (var c = 0; c < first.Count; c++)
        {
            var n = Norm(first[c].Text);
            if (n.Length == 0)
            {
                continue;
            }

            var idx = normalizedTemplate.IndexOf(n);
            if (idx >= 0)
            {
                map[c] = idx + 1;
            }
        }

        var nonEmpty = first.Count(x => x.Text.Trim().Length > 0);
        return nonEmpty > 0 && map.Count * 2 >= nonEmpty && map.Count >= 1 && templateHeaders.Count > 0 ? map : null;
    }

    private static bool LooksLikeHeader(IReadOnlyList<IReadOnlyList<SourceCell>> rows)
    {
        if (rows.Count < 2)
        {
            return false;
        }

        static bool IsNumeric(SourceCell c) => ValueClassifier.Classify(c.Text).Kind is ValueKind.Integer or ValueKind.Decimal or ValueKind.AmbiguousNumber;
        var firstHasNumbers = rows[0].Any(IsNumeric);
        var restHaveNumbers = rows.Skip(1).Take(5).Any(r => r.Any(IsNumeric));
        var firstFilled = rows[0].Count(c => c.Text.Trim().Length > 0);
        return !firstHasNumbers && restHaveNumbers && firstFilled >= Math.Max(1, rows[0].Count / 2);
    }

    private static string Norm(string s) => InstructionParser.Fold(s.Trim());
}
