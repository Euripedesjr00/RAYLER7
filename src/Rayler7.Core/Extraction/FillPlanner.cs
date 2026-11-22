using Rayler7.Core.Mapping;

namespace Rayler7.Core.Extraction;

public sealed record PlannedWrite(int Row, int Column, string Text, Guid RecordId, bool FlaggedDoubt);

public sealed record FillPlan(
    IReadOnlyList<PlannedWrite> Writes,
    int RowsAdded,
    int EmptyKept,
    int ExcludedNoTarget,
    int ExcludedRejected,
    int ExcludedOpenDoubts,
    int FlaggedDoubtsIncluded);

/// <summary>
/// Planeja onde cada registro selecionado será gravado: cada linha de origem vira uma nova linha
/// ao final dos dados do modelo, na mesma ordem. Dúvidas abertas só entram com autorização explícita,
/// e ficam sinalizadas.
/// </summary>
public static class FillPlanner
{
    public static FillPlan Plan(IEnumerable<ExtractedRecord> selected, int firstFreeRow, bool allowOpenDoubts)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(firstFreeRow);
        var writes = new List<PlannedWrite>();
        int empty = 0, noTarget = 0, rejected = 0, openDoubts = 0, flagged = 0;
        var rowOf = new Dictionary<(string, int?, int?), int>();
        var next = firstFreeRow;

        foreach (var r in selected)
        {
            if (r.Status == RecordStatus.Rejected)
            {
                rejected++;
                continue;
            }

            if (r.TargetColumn is null)
            {
                noTarget++;
                continue;
            }

            if (r.HasOpenDoubt && !allowOpenDoubts)
            {
                openDoubts++;
                continue;
            }

            var key = (r.SourceFile, r.Page, r.Row);
            if (!rowOf.TryGetValue(key, out var row))
            {
                row = next++;
                rowOf[key] = row;
            }

            var text = r.EffectiveText;
            if (text.Length == 0)
            {
                empty++;
                continue;
            }

            if (r.HasOpenDoubt)
            {
                flagged++;
            }

            writes.Add(new PlannedWrite(row, ColumnLetters.ToIndex(r.TargetColumn), text, r.Id, r.HasOpenDoubt));
        }

        return new FillPlan(writes, rowOf.Count, empty, noTarget, rejected, openDoubts, flagged);
    }
}
