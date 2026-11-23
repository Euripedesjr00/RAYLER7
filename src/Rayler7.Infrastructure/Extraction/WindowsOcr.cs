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
