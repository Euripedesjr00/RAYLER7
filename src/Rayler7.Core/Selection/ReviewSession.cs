using Rayler7.Core.Extraction;

namespace Rayler7.Core.Selection;

/// <summary>
/// Mantém registros, filtro, paginação e seleção de forma independente.
/// A seleção é guardada por identificador — trocar de página ou de filtro nunca a perde,
/// e "Selecionar todos" opera sobre o conjunto completo, não sobre a página visível.
/// </summary>
public sealed class ReviewSession
{
    private readonly List<ExtractedRecord> _records = [];
    private readonly Dictionary<Guid, ExtractedRecord> _byId = [];
    private readonly HashSet<Guid> _selected = [];
    private Func<ExtractedRecord, bool>? _filter;
    private bool _selectAllActive;
    private int _addedSinceSelectAll;

    public event EventHandler? Changed;

    public IReadOnlyList<ExtractedRecord> Records => _records;

    public bool HasActiveFilter => _filter is not null;

    /// <summary>Rejeitados nunca são elegíveis para o preenchimento nem para "Selecionar todos".</summary>
    public static bool IsEligible(ExtractedRecord r) => r.Status != RecordStatus.Rejected;

    public void AddRecords(IEnumerable<ExtractedRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var r in records)
        {
            if (!_byId.TryAdd(r.Id, r))
            {
                throw new InvalidOperationException($"Registro duplicado: {r.Id}");
            }

            _records.Add(r);
            if (_selectAllActive)
            {
                _addedSinceSelectAll++;
            }
        }

        // Novos registros entram desmarcados; o contador avisa o usuário.
        OnChanged();
    }

    public void SetFilter(Func<ExtractedRecord, bool>? filter)
    {
        _filter = filter;
        OnChanged();
    }

    public IReadOnlyList<ExtractedRecord> FilteredRecords() =>
        _filter is null ? _records : _records.Where(_filter).ToList();

    public int PageCount(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var n = FilteredRecords().Count;
        return Math.Max(1, (n + pageSize - 1) / pageSize);
    }

    public IReadOnlyList<ExtractedRecord> Page(int pageIndex, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        return FilteredRecords().Skip(pageIndex * pageSize).Take(pageSize).ToList();
    }

    public bool IsSelected(Guid id) => _selected.Contains(id);

    public void Select(Guid id)
    {
        var r = Get(id);
        if (!IsEligible(r))
        {
            throw new InvalidOperationException("Registros rejeitados não podem ser selecionados.");
        }

        _selected.Add(id);
        OnChanged();
    }

    public void Deselect(Guid id)
    {
        _ = Get(id);
        _selected.Remove(id);
        _selectAllActive = false;
        OnChanged();
    }

    public void Toggle(Guid id)
    {
        if (IsSelected(id))
        {
            Deselect(id);
        }
        else
        {
            Select(id);
        }
    }

    /// <summary>
    /// Seleciona todos os registros elegíveis no escopo escolhido — em TODAS as páginas.
    /// Se houver filtro ativo, o escopo é obrigatório e explícito.
    /// </summary>
    public void SelectAll(SelectionScope scope)
    {
        var source = scope == SelectionScope.FilteredOnly ? FilteredRecords() : _records;
        foreach (var r in source.Where(IsEligible))
        {
            _selected.Add(r.Id);
        }

        _selectAllActive = scope == SelectionScope.AllRecords;
        _addedSinceSelectAll = 0;
        OnChanged();
    }

    public void DeselectAll(SelectionScope scope)
    {
        if (scope == SelectionScope.FilteredOnly)
        {
            foreach (var r in FilteredRecords())
            {
                _selected.Remove(r.Id);
            }
        }
        else
        {
            _selected.Clear();
        }

        _selectAllActive = false;
        _addedSinceSelectAll = 0;
        OnChanged();
    }

    /// <summary>Deve ser chamado após rejeitar um registro, para retirá-lo da seleção.</summary>
    public void RefreshAfterStatusChange()
    {
        _selected.RemoveWhere(id => !IsEligible(_byId[id]));
        OnChanged();
    }

    public bool AreAllEligibleSelected(SelectionScope scope)
    {
        var source = scope == SelectionScope.FilteredOnly ? FilteredRecords() : _records;
        var eligible = source.Where(IsEligible).ToList();
        return eligible.Count > 0 && eligible.All(r => _selected.Contains(r.Id));
    }

    public IReadOnlyList<ExtractedRecord> SelectedRecords() =>
        _records.Where(r => _selected.Contains(r.Id)).ToList();

    public SelectionSummary Summary()
    {
        var total = _records.Count;
        var selected = _selected.Count;
        return new SelectionSummary(
            total,
            selected,
            total - selected,
            _records.Count(r => r.Status == RecordStatus.Pending),
            _records.Count(r => r.Status == RecordStatus.Rejected),
            _records.Count(r => r.Status == RecordStatus.Doubtful),
            AreAllEligibleSelected(SelectionScope.AllRecords),
            _addedSinceSelectAll);
    }

    private ExtractedRecord Get(Guid id) =>
        _byId.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"Registro inexistente: {id}");

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
