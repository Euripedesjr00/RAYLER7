using Rayler7.Core.Audit;
using Rayler7.Core.Values;

namespace Rayler7.Core.Extraction;

/// <summary>
/// Um dado extraído com sua origem completa. O valor original é imutável;
/// toda mudança de status ou correção passa por métodos que exigem usuário e geram auditoria.
/// </summary>
public sealed class ExtractedRecord
{
    private readonly List<Doubt> _doubts;

    public ExtractedRecord(Guid id, string sourceFile, int? page, int? row, string? column, string fieldName,
        PreservedValue value, double? confidence, IEnumerable<Doubt>? doubts = null, string? targetColumn = null, CellBox? box = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        Id = id;
        SourceFile = sourceFile;
        Page = page;
        Row = row;
        Column = column;
        FieldName = fieldName;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Confidence = confidence;
        TargetColumn = targetColumn;
        Box = box;
        _doubts = doubts?.ToList() ?? [];
        Status = _doubts.Count > 0 || value.Kind is ValueKind.AmbiguousNumber or ValueKind.Illegible
            ? RecordStatus.Doubtful
            : RecordStatus.Pending;
    }

    public Guid Id { get; }
    public string SourceFile { get; }
    public int? Page { get; }
    public int? Row { get; }
    public string? Column { get; }
    public string FieldName { get; }
    public PreservedValue Value { get; }
    public double? Confidence { get; }

    /// <summary>Onde o valor está na imagem de origem (quando veio de OCR).</summary>
    public CellBox? Box { get; }
    public string? TargetColumn { get; private set; }
    public RecordStatus Status { get; private set; }
    public string? CorrectedText { get; private set; }
    public bool LeaveEmpty { get; private set; }
    public IReadOnlyList<Doubt> Doubts => _doubts;

    /// <summary>Texto que irá para a planilha: correção autorizada, vazio autorizado ou o original.</summary>
    public string EffectiveText => LeaveEmpty ? string.Empty : CorrectedText ?? Value.Original;

    public bool HasOpenDoubt => Status == RecordStatus.Doubtful;

    /// <summary>Acrescenta uma dúvida encontrada depois da leitura (ex.: linha repetida). Registros já decididos não mudam.</summary>
    public void AddDoubt(Doubt doubt)
    {
        ArgumentNullException.ThrowIfNull(doubt);
        _doubts.Add(doubt);
        if (Status == RecordStatus.Pending)
        {
            Status = RecordStatus.Doubtful;
        }
    }

    /// <summary>Usado somente ao recarregar do banco um estado já auditado.</summary>
    public void RestoreState(RecordStatus status, string? correctedText, bool leaveEmpty)
    {
        Status = status;
        CorrectedText = correctedText;
        LeaveEmpty = leaveEmpty;
    }

    public void ConfirmAsIdentified(string user, Guid projectId, IAuditLog audit)
    {
        Require(user, audit);
        var before = Status;
        Status = RecordStatus.Confirmed;
        audit.Append(Entry(projectId, user, "Confirmar valor identificado", before.ToString(), Value.Original, null));
    }

    public void Correct(string newText, string user, Guid projectId, IAuditLog audit, string? reason = null)
    {
        Require(user, audit);
        ArgumentNullException.ThrowIfNull(newText);
        var before = EffectiveText;
        CorrectedText = newText;
        LeaveEmpty = false;
        Status = RecordStatus.CorrectedManually;
        audit.Append(Entry(projectId, user, "Correção manual", before, newText, reason));
    }

    public void MarkLeaveEmpty(string user, Guid projectId, IAuditLog audit, string? reason = null)
    {
        Require(user, audit);
        var before = EffectiveText;
        LeaveEmpty = true;
        Status = RecordStatus.CorrectedManually;
        audit.Append(Entry(projectId, user, "Manter célula vazia", before, string.Empty, reason));
    }

    public void Reject(string user, Guid projectId, IAuditLog audit, string? reason = null)
    {
        Require(user, audit);
        var before = Status;
        Status = RecordStatus.Rejected;
        audit.Append(Entry(projectId, user, "Rejeitar registro", before.ToString(), RecordStatus.Rejected.ToString(), reason));
    }

    public void SetTargetColumn(string? column, string user, Guid projectId, IAuditLog audit)
    {
        Require(user, audit);
        var before = TargetColumn;
        TargetColumn = column;
        audit.Append(Entry(projectId, user, "Alterar coluna de destino", before, column, null));
    }

    private AuditEntry Entry(Guid projectId, string user, string op, string? oldV, string? newV, string? reason) =>
        new(projectId, user, DateTimeOffset.Now, op, $"registro:{Id}", oldV, newV, $"{SourceFile}#p{Page}l{Row}c{Column}", reason);

    private static void Require(string user, IAuditLog audit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentNullException.ThrowIfNull(audit);
    }
}
