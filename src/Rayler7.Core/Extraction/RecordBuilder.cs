using Rayler7.Core.Mapping;
using Rayler7.Core.Semantics;
using Rayler7.Core.Values;

namespace Rayler7.Core.Extraction;

public sealed record BuildResult(IReadOnlyList<ExtractedRecord> Records, IReadOnlyList<string> Warnings, bool HeaderRowSkipped, bool MatchedByHeader);

/// <summary>
/// Transforma tabelas lidas em registros revisáveis. Etapas, todas locais e sem IA:
/// 1) fichas "Campo: Valor" viram tabela; 2) acha o cabeçalho real, pulando títulos acima dele;
/// 3) associa cada coluna da origem a uma coluna do modelo pelo significado (nome, sinônimo,
/// instruções e tipo dos valores) — ou pela ordem, quando não há cabeçalho;
/// 4) pula cabeçalhos repetidos no meio dos dados. Nunca cria colunas: o que sobra vira dúvida.
/// </summary>
public static class RecordBuilder
{
    public static BuildResult Build(SourceTable table, IReadOnlyList<string> templateHeaders, IReadOnlyCollection<ColumnMapping> mappings, DoubtDetector? detector = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(templateHeaders);
        ArgumentNullException.ThrowIfNull(mappings);
        detector ??= new DoubtDetector();
        table = KeyValueForms.TryConvert(table) ?? table;
        var warnings = new List<string>(table.Warnings);
        var records = new List<ExtractedRecord>();
        var where = $"{table.SourceFile}{(table.Page is { } pg ? $" (pág. {pg})" : string.Empty)}";
        if (table.Rows.Count == 0)
        {
            warnings.Add($"{table.SourceFile}: nenhuma linha encontrada.");
            return new BuildResult(records, warnings, false, false);
        }

        var templateCols = Math.Max(templateHeaders.Count, mappings.Count == 0 ? 0 : mappings.Max(m => m.ColumnIndex));
        var headerRow = HeaderDetector.Detect(table.Rows);
        var firstData = headerRow is { } h ? h + 1 : 0;
        var width = table.Rows.Skip(firstData).Select(r => r.Count).DefaultIfEmpty(0).Max();
        var headerNormEarly = headerRow is { } h0 ? table.Rows[h0].Select(c => TextNorm.Token(c.Text)).ToList() : null;
        var sampleRows = table.Rows.Skip(firstData).Where(r => headerNormEarly is null || !IsRepeatedHeader(r, headerNormEarly)).Take(30).ToList();
        var samples = Enumerable.Range(0, width)
            .Select(c => (IReadOnlyList<string>)sampleRows
                .Select(r => c < r.Count ? r[c].Text : string.Empty).Where(t => t.Trim().Length > 0).Take(5).ToList())
            .ToList();

        MatchPlan plan;
        IReadOnlyList<string>? sourceHeaders = null;
        if (headerRow is { } hr)
        {
            sourceHeaders = table.Rows[hr].Select(c => c.Text).ToList();
            if (hr > 0)
            {
                warnings.Add($"{where}: {hr} linha(s) de título acima do cabeçalho foram ignoradas.");
            }

            plan = ColumnMatcher.Match(sourceHeaders, samples, templateHeaders, mappings);
            if (plan.ByName)
            {
                plan = ColumnMatcher.FillByPosition(plan, width, samples, templateHeaders, mappings);
                var named = plan.Matches.Where(m => m.Reason is MatchReason.Synonym or MatchReason.Instruction).ToList();
                if (named.Count > 0)
                {
                    warnings.Add($"{where}: associadas pelo significado — " + string.Join("; ", named.Select(m =>
                        $"\"{sourceHeaders[m.SourceColumn].Trim()}\" → {ColumnLetters.FromIndex(m.TargetColumn)} ({Header(templateHeaders, m.TargetColumn)})")));
                }
            }
            else
            {
                plan = ColumnMatcher.ByPosition(width, templateCols);
                warnings.Add($"{where}: os nomes das colunas não conferem com o modelo; colunas associadas pela ordem e cabeçalho da origem ignorado.");
            }
        }
        else
        {
            plan = ColumnMatcher.ByPosition(width, templateCols);
            warnings.Add($"{where}: sem cabeçalho; colunas associadas pela ordem ao modelo.");
        }

        var headerNorm = headerNormEarly;
        var modeWidth = table.Rows.Skip(firstData).Where(r => r.Any(c => c.Text.Length > 0))
            .GroupBy(r => r.Count).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var repeatedHeaders = 0;
        for (var r = firstData; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            if (row.All(c => c.Text.Trim().Length == 0))
            {
                continue; // linha totalmente vazia na origem: não vira registro
            }

            if (headerNorm is not null && IsRepeatedHeader(row, headerNorm))
            {
                repeatedHeaders++;
                continue;
            }

            for (var c = 0; c < row.Count; c++)
            {
                var cell = row[c];
                var match = plan.For(c);
                var target = match?.TargetColumn;
                var mapping = target is { } ti ? mappings.FirstOrDefault(m => m.ColumnIndex == ti) : null;
                var expectsNumber = mapping is { ExpectNumeric: true } ||
                                    (target is { } tn && tn - 1 < templateHeaders.Count && SemanticRoles.ExpectsNumber(SemanticRoles.Infer(templateHeaders[tn - 1])));
                var expectation = expectsNumber ? new ColumnExpectation(true, null) : null;
                var doubts = cell.Text.Length == 0 && cell.Confidence is null
                    ? new List<Doubt>()
                    : detector.Inspect(new OcrReading(cell.Text, cell.Characters, cell.Confidence, cell.AlternateText), expectation).ToList();
                if (cell.SecondEngineMissed)
                {
                    doubts.Add(new Doubt(DoubtKind.EngineDisagreement,
                        "O segundo motor de leitura não reconheceu este trecho. Confira com a imagem.", cell.Text, null));
                }

                if (target is null && cell.Text.Trim().Length > 0)
                {
                    var label = sourceHeaders is not null && c < sourceHeaders.Count && sourceHeaders[c].Trim().Length > 0
                        ? $"A coluna \"{sourceHeaders[c].Trim()}\" da origem" : "Esta coluna da origem";
                    doubts.Add(new Doubt(DoubtKind.NoTargetColumn,
                        $"{label} não tem coluna correspondente no modelo. Nenhuma coluna nova é criada sem sua decisão.", cell.Text, null));
                }
                else if (match is { Reason: MatchReason.Position } && plan.ByName && cell.Text.Trim().Length > 0)
                {
                    doubts.Add(new Doubt(DoubtKind.PositionalMatch,
                        $"Coluna \"{(sourceHeaders is not null && c < sourceHeaders.Count ? sourceHeaders[c].Trim() : string.Empty)}\" da origem associada a {ColumnLetters.FromIndex(target!.Value)} ({Header(templateHeaders, target.Value)}) só pela posição. Confira.",
                        cell.Text, null));
                }

                if (c == 0 && modeWidth > 0 && row.Count != modeWidth)
                {
                    doubts.Add(new Doubt(DoubtKind.RowShape,
                        $"Esta linha tem {row.Count} coluna(s); a maioria das linhas tem {modeWidth}. Confira o alinhamento.", cell.Text, null));
                }

                var field = mapping?.Meaning ?? (target is { } tt && tt - 1 < templateHeaders.Count ? templateHeaders[tt - 1]
                    : sourceHeaders is not null && c < sourceHeaders.Count && sourceHeaders[c].Trim().Length > 0 ? sourceHeaders[c].Trim()
                    : $"Coluna {c + 1} da origem");
                records.Add(new ExtractedRecord(Guid.NewGuid(), table.SourceFile, table.Page, r + 1, ColumnLetters.FromIndex(c + 1), field,
                    ValueClassifier.Classify(cell.Text), cell.Confidence, doubts, target is { } x ? ColumnLetters.FromIndex(x) : null, cell.Box));
            }
        }

        if (repeatedHeaders > 0)
        {
            warnings.Add($"{where}: {repeatedHeaders} cabeçalho(s) repetido(s) no meio dos dados foram ignorados.");
        }

        return new BuildResult(records, warnings, headerRow is not null, plan.ByName);
    }

    private static string Header(IReadOnlyList<string> headers, int col) => col - 1 < headers.Count ? headers[col - 1] : string.Empty;

    /// <summary>Linha igual ao cabeçalho (páginas de PDF ou arquivos juntados repetem o cabeçalho).</summary>
    private static bool IsRepeatedHeader(IReadOnlyList<SourceCell> row, List<string> headerNorm)
    {
        var filled = row.Count(c => c.Text.Trim().Length > 0);
        if (filled < 2)
        {
            return false;
        }

        var same = 0;
        for (var c = 0; c < row.Count && c < headerNorm.Count; c++)
        {
            var n = TextNorm.Token(row[c].Text);
            if (n.Length > 0 && n == headerNorm[c])
            {
                same++;
            }
        }

        return same >= Math.Max(2, (int)Math.Ceiling(filled * 0.75));
    }
}
