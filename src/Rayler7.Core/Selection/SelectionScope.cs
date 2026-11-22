namespace Rayler7.Core.Selection;

/// <summary>
/// Quando há filtro ativo, a ação "Selecionar todos" exige que o usuário escolha explicitamente o escopo.
/// </summary>
public enum SelectionScope
{
    /// <summary>Todos os registros do processamento, em todas as páginas, independentemente do filtro.</summary>
    AllRecords,
    /// <summary>Somente os registros que satisfazem o filtro atual (em todas as páginas do filtro).</summary>
    FilteredOnly,
}

public sealed record SelectionSummary(
    int TotalExtracted,
    int Selected,
    int NotSelected,
    int PendingReview,
    int Rejected,
    int Doubtful,
    bool AllEligibleSelected,
    int NewUnselectedSinceSelectAll);
