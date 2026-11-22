namespace Rayler7.Core.Extraction;

/// <summary>Confiança (0..1) de um caractere lido pelo OCR.</summary>
public sealed record CharConfidence(char Character, double Confidence);

/// <summary>
/// Leitura de um trecho por OCR. <see cref="AlternateText"/> vem de um segundo motor
/// (ex.: OCR do Windows) — divergência entre motores vira dúvida.
/// </summary>
public sealed record OcrReading(string Text, IReadOnlyList<CharConfidence>? Characters, double? WordConfidence, string? AlternateText);

/// <summary>O que se espera de uma coluna, configurado no mapeamento.</summary>
public sealed record ColumnExpectation(bool Numeric, int? TypicalDecimalPlaces);
