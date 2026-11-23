using Rayler7.Core.Extraction;

namespace Rayler7.Infrastructure.Extraction;

/// <summary>
/// Lê imagens com dois motores gratuitos e locais. O Tesseract (com confiança por caractere) é o principal;
/// o OCR do Windows confere a leitura. Quando discordam, o valor vira dúvida — nada é decidido sozinho.
/// </summary>
public static class ImageExtractor
{
    public static ExtractionOutput Extract(string path, string name, CancellationToken ct)
    {
        var warnings = new List<string>();
        List<OcrWord>? tess = null;
        List<OcrWord>? win = null;
        if (TesseractOcr.IsAvailable)
        {
            try
            {
                tess = TesseractOcr.Recognize(path);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or DllNotFoundException or BadImageFormatException)
            {
                warnings.Add($"{name}: Tesseract não conseguiu ler a imagem ({ex.Message}).");
            }
        }
        else
        {
            warnings.Add("Tesseract indisponível (dados de idioma ausentes).");
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            win = WindowsOcr.RecognizeAsync(path).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            warnings.Add($"{name}: OCR do Windows não conseguiu ler a imagem ({ex.Message}).");
        }

        List<IReadOnlyList<SourceCell>> rows;
        if (tess is { Count: > 0 })
        {
            rows = LayoutTable.Build(tess, win is null ? null : LayoutTable.MatchFrom(win));
            if (win is null)
            {
                warnings.Add($"{name}: lido só pelo Tesseract, sem conferência cruzada.");
            }
        }
        else if (win is { Count: > 0 })
        {
            rows = LayoutTable.Build(win);
            warnings.Add($"{name}: lido só pelo OCR do Windows, que não informa confiança. Confira todos os valores com a imagem.");
        }
        else
        {
            throw new InvalidDataException($"{name}: nenhum texto foi reconhecido na imagem.");
        }

        warnings.Add($"{name}: leitura por OCR — confira os valores com a imagem antes de confirmar.");
        return new ExtractionOutput([new SourceTable(name, 1, "Imagem", rows, [])], warnings, 0, 1);
    }
}
