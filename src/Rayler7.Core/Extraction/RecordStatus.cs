namespace Rayler7.Core.Extraction;

public enum RecordStatus
{
    /// <summary>Extraído, ainda não revisado.</summary>
    Pending,
    /// <summary>Confirmado pelo usuário como está.</summary>
    Confirmed,
    /// <summary>Possui dúvida aberta — não pode ir para a planilha sem decisão.</summary>
    Doubtful,
    /// <summary>Rejeitado pelo usuário; nunca é usado no preenchimento.</summary>
    Rejected,
    /// <summary>Corrigido manualmente com registro no histórico.</summary>
    CorrectedManually,
}

public enum DoubtKind
{
    LowConfidence,
    AmbiguousCharacter,
    EngineDisagreement,
    TypeMismatch,
    AmbiguousSeparator,
    DecimalPlacesMismatch,
    Illegible,
    NoTargetColumn,
    RowShape,
    /// <summary>Coluna associada pela posição, sem nome reconhecido: conferir.</summary>
    PositionalMatch,
    /// <summary>Linha com os mesmos valores de outra já existente.</summary>
    DuplicateRow,
}

/// <summary>Dúvida identificada na extração. O sistema nunca a resolve sozinho.</summary>
public sealed record Doubt(DoubtKind Kind, string Message, string IdentifiedValue, string? SuggestedValue);
