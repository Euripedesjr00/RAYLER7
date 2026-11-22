namespace Rayler7.Core.Extraction;

/// <summary>Célula lida de um arquivo de origem, com a confiança da leitura quando vier de OCR.</summary>
public sealed record SourceCell(string Text, double? Confidence = null, string? AlternateText = null, IReadOnlyList<CharConfidence>? Characters = null);

/// <summary>Tabela encontrada em um arquivo (uma aba, uma página de PDF, uma tabela do Word, uma imagem).</summary>
public sealed record SourceTable(string SourceFile, int? Page, string? Label, IReadOnlyList<IReadOnlyList<SourceCell>> Rows, IReadOnlyList<string> Warnings);
