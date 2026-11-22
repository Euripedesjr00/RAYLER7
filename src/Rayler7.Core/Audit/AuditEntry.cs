namespace Rayler7.Core.Audit;

/// <summary>Registro imutável de uma alteração autorizada.</summary>
public sealed record AuditEntry(
    Guid ProjectId,
    string User,
    DateTimeOffset Timestamp,
    string Operation,
    string Target,
    string? OldValue,
    string? NewValue,
    string Origin,
    string? Reason);

public interface IAuditLog
{
    void Append(AuditEntry entry);

    IReadOnlyList<AuditEntry> Entries(Guid projectId);
}

/// <summary>Implementação em memória (usada em testes e como cache da interface).</summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly List<AuditEntry> _entries = [];
    private readonly Lock _gate = new();

    public void Append(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    public IReadOnlyList<AuditEntry> Entries(Guid projectId)
    {
        lock (_gate)
        {
            return _entries.Where(e => e.ProjectId == projectId).ToList();
        }
    }
}
