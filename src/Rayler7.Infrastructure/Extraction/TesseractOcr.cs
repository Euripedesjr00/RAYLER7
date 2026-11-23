using Rayler7.Core.Extraction;
using Tesseract;

namespace Rayler7.Infrastructure.Extraction;

/// <summary>Tesseract (código aberto): informa a confiança de cada caractere, usada para apontar dúvidas.</summary>
public static class TesseractOcr
{
    public static string TessdataPath => Path.Combine(AppContext.BaseDirectory, "tessdata");

    public static bool IsAvailable => File.Exists(Path.Combine(TessdataPath, "por.traineddata"));

    public static List<OcrWord> Recognize(string path)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Os dados de idioma do Tesseract (por.traineddata) não foram encontrados.");
        }

        using var engine = new TesseractEngine(TessdataPath, "por", EngineMode.Default);
        engine.SetVariable("preserve_interword_spaces", "1");
        using var image = Pix.LoadFromFile(path);
        using var page = engine.Process(image, PageSegMode.Auto);
        using var it = page.GetIterator();
        var words = new List<OcrWord>();
        it.Begin();
        do
        {
            if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
            {
                continue;
            }

            var text = it.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var wordConf = it.GetConfidence(PageIteratorLevel.Word) / 100.0;
            var chars = new List<CharConfidence>();
            do
            {
                var sym = it.GetText(PageIteratorLevel.Symbol);
                if (!string.IsNullOrEmpty(sym))
                {
                    var conf = it.GetConfidence(PageIteratorLevel.Symbol) / 100.0;
                    chars.AddRange(sym.Select(c => new CharConfidence(c, conf)));
                }
            }
            while (!it.IsAtFinalOf(PageIteratorLevel.Word, PageIteratorLevel.Symbol) && it.Next(PageIteratorLevel.Word, PageIteratorLevel.Symbol));

            words.Add(new OcrWord(text, box.X1, box.Y1, box.Width, box.Height, wordConf, chars.Count == text.Length ? chars : null));
        }
        while (it.Next(PageIteratorLevel.Word));

        return words;
    }
}
