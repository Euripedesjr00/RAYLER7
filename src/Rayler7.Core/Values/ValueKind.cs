namespace Rayler7.Core.Values;

/// <summary>Classificação de um valor lido. A classificação nunca altera o texto original.</summary>
public enum ValueKind
{
    /// <summary>Célula vazia (ou só espaços).</summary>
    Empty,
    /// <summary>Número inteiro sem zeros à esquerda (ex.: 0, 25, -3).</summary>
    Integer,
    /// <summary>Número decimal com separador identificado sem ambiguidade.</summary>
    Decimal,
    /// <summary>Número cujo separador pode ser decimal ou de milhar (ex.: 1.234). Exige decisão do usuário.</summary>
    AmbiguousNumber,
    /// <summary>Sequência numérica que deve ser tratada como código (ex.: 001, 00025).</summary>
    Code,
    /// <summary>Data reconhecida (dd/mm/aaaa, dd/mm/aa ou aaaa-mm-dd).</summary>
    Date,
    /// <summary>Texto livre.</summary>
    Text,
    /// <summary>Valor marcado como ilegível pela extração.</summary>
    Illegible,
}

/// <summary>Separador decimal identificado no texto original.</summary>
public enum DecimalSeparator
{
    None,
    Comma,
    Dot,
    Ambiguous,
}
