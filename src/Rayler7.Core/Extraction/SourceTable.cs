namespace Rayler7.Core.Extraction;

/// <summary>Célula lida de um arquivo de origem, com a confiança da leitura quando vier de OCR.</summary>
/// <summary>Posição de uma célula na imagem de origem (pixels), para mostrar o trecho original na conferência.</summary>
public sealed record CellBox(double X, double Y, double Width, double Height);

public sealed record SourceCell(string Text, double? Confidence = null, string? AlternateText = null, IReadOnlyList<CharConfidence>? Characters = null, CellBox? Box = null, bool SecondEngineMissed = false);

/// <summary>Tabela encontrada em um arquivo (uma aba, uma página de PDF, uma tabela do Word, uma imagem).</summary>
public sealed record SourceTable(string SourceFile, int? Page, string? Label, IReadOnlyList<IReadOnlyList<SourceCell>> Rows, IReadOnlyList<string> Warnings);
