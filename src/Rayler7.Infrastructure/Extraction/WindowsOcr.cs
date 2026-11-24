using Rayler7.Core.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Rayler7.Infrastructure.Extraction;

/// <summary>OCR que já vem no Windows 10/11: gratuito e local. Não informa confiança por caractere.</summary>
public static class WindowsOcr
{
    public static bool IsAvailable => CreateEngine() is not null;

    public static string? LanguageTag => CreateEngine()?.RecognizerLanguage.LanguageTag;

    private static OcrEngine? CreateEngine()
    {
        var pt = new Language("pt-BR");
        return OcrEngine.IsLanguageSupported(pt)
            ? OcrEngine.TryCreateFromLanguage(pt)
            : OcrEngine.TryCreateFromUserProfileLanguages();
    }

    public static async Task<List<OcrWord>> RecognizeAsync(string path)
    {
        var engine = CreateEngine() ?? throw new InvalidOperationException("O OCR do Windows não está disponível neste computador.");
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var max = OcrEngine.MaxImageDimension;
        var scale = Math.Min(1.0, (double)max / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale),
            ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await engine.RecognizeAsync(bitmap);
        return ToWords(result, scale);
    }

    /// <summary>Qualidade de uma leitura rápida, usada para descobrir a orientação certa da foto.</summary>
    public readonly record struct OcrScore(int GoodWords, int Chars);

    public static async Task<OcrScore> ScoreAsync(GrayImage image)
    {
        var engine = CreateEngine() ?? throw new InvalidOperationException("O OCR do Windows não está disponível neste computador.");
        var max = (int)OcrEngine.MaxImageDimension;
        if (Math.Max(image.Width, image.Height) > max)
        {
            image = image.Scale((double)max / Math.Max(image.Width, image.Height));
        }

        using var bitmap = ImagePreprocessor.ToSoftwareBitmap(image);
        var result = await engine.RecognizeAsync(bitmap);
        var good = 0;
        var chars = 0;
        foreach (var w in result.Lines.SelectMany(l => l.Words))
        {
            var t = w.Text;
            var alnum = t.Count(char.IsLetterOrDigit);
            if (t.Length >= 2 && alnum >= t.Length * 0.7)
            {
                good++;
                chars += alnum;
            }
        }

        return new OcrScore(good, chars);
    }

    private static List<OcrWord> ToWords(OcrResult result, double scale)
    {
        var words = new List<OcrWord>();
        foreach (var line in result.Lines)
        {
            foreach (var w in line.Words)
            {
                var r = w.BoundingRect;
                words.Add(new OcrWord(w.Text, r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale, null, null));
            }
        }

        return words;
    }
}
