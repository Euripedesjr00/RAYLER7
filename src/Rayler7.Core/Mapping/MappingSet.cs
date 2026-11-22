using Rayler7.Core.Audit;

namespace Rayler7.Core.Mapping;

/// <summary>
/// Conjunto de mapeamentos de um projeto. Depois de confirmado, fica travado:
/// qualquer alteração exige destravar explicitamente, com registro no histórico.
/// </summary>
public sealed class MappingSet
{
    private readonly SortedDictionary<int, ColumnMapping> _items = [];

    public bool IsConfirmed { get; private set; }

    public IReadOnlyCollection<ColumnMapping> Items => _items.Values;

    public ColumnMapping? Get(int columnIndex) => _items.GetValueOrDefault(columnIndex);

    public void Set(ColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        EnsureEditable();
        _items[mapping.ColumnIndex] = mapping;
    }

    public bool Remove(int columnIndex)
    {
        EnsureEditable();
        return _items.Remove(columnIndex);
    }

    public void Confirm(string user, Guid projectId, IAuditLog audit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentNullException.ThrowIfNull(audit);
        IsConfirmed = true;
        audit.Append(new AuditEntry(projectId, user, DateTimeOffset.Now, "Confirmar mapeamento", "mapeamento",
            null, string.Join("; ", _items.Values.Select(m => $"{m.ColumnLetter}={m.Meaning}")), "usuário", null));
    }

    public void Unlock(string user, Guid projectId, IAuditLog audit, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(audit);
        IsConfirmed = false;
        audit.Append(new AuditEntry(projectId, user, DateTimeOffset.Now, "Destravar mapeamento", "mapeamento",
            null, null, "usuário", reason));
    }

    private void EnsureEditable()
    {
        if (IsConfirmed)
        {
            throw new InvalidOperationException("O mapeamento está confirmado. Destrave-o explicitamente antes de alterar.");
        }
    }
}
