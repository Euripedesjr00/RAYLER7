using Rayler7.Core.Extraction;

namespace Rayler7.Infrastructure.Extraction;

/// <summary>Palavra lida por OCR, com posição em pixels (Y cresce para baixo).</summary>
public sealed record OcrWord(string Text, double X, double Y, double Width, double Height, double? Confidence, IReadOnlyList<CharConfidence>? Characters)
{
    public double Right => X + Width;

    public double CenterY => Y + (Height / 2);
}
