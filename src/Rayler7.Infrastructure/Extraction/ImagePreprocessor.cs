using Rayler7.Core.Imaging;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Rayler7.Infrastructure.Extraction;

public sealed record PreparedImage(string OcrPath, IReadOnlyList<string> Applied, IReadOnlyList<string> Warnings);

/// <summary>
/// Prepara a imagem antes do OCR: aplica a rotação gravada pela câmera (EXIF), corrige fotos de lado ou
/// de cabeça para baixo, ajusta luz e contraste, endireita, amplia e realça. O resultado é salvo ao lado
/// ("{arquivo}.ocr.png") e é a MESMA imagem usada pelos dois motores e pelos recortes de conferência.
/// </summary>
public static class ImagePreprocessor
{
    private const long MaxDecodePixels = 30_000_000;
    private const int GoodWordsEnough = 8;

    /// <summary>Imagem preparada mais recente para este arquivo (null se ainda não foi processado).</summary>
    public static string? LatestOcrPath(string imagePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(imagePath));
        if (dir is null || !Directory.Exists(dir))
        {
            return null;
        }

        return Directory.EnumerateFiles(dir, Path.GetFileName(imagePath) + ".ocr*.png")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Grava a imagem preparada. O decodificador do Windows pode manter a versão anterior bloqueada por
    /// alguns instantes; nesse caso grava com outro nome em vez de falhar.
    /// </summary>
    private static async Task<string> WriteOutputAsync(string imagePath, byte[] png)
    {
        for (var i = 0; ; i++)
        {
            var candidate = i == 0 ? imagePath + ".ocr.png" : $"{imagePath}.ocr{i}.png";
            try
            {
                await File.WriteAllBytesAsync(candidate, png);
                return candidate;
            }
            catch (IOException) when (i < 20)
            {
            }
            catch (UnauthorizedAccessException) when (i < 20)
            {
            }
        }
    }

    public static PreparedImage Prepare(string imagePath) => PrepareAsync(imagePath).GetAwaiter().GetResult();

    public static async Task<PreparedImage> PrepareAsync(string imagePath)
    {
        var original = await DecodeAsync(imagePath);
        var prepared = ImageAnalysis.Prepare(original);
        var applied = prepared.Applied.ToList();
        var warnings = prepared.Warnings.ToList();
        var image = prepared.Image;

        // Foto de lado ou invertida: o OCR do Windows (rápido) compara as 4 orientações só quando a leitura normal é fraca.
        if (WindowsOcr.IsAvailable)
        {
            var baseScore = await WindowsOcr.ScoreAsync(image);
            if (baseScore.GoodWords < GoodWordsEnough)
            {
                var best = (Quarters: 0, Score: baseScore);
                for (var q = 1; q <= 3; q++)
                {
                    var s = await WindowsOcr.ScoreAsync(image.RotateQuarter(q));
                    if (s.Chars > best.Score.Chars)
                    {
                        best = (q, s);
                    }
                }

                if (best.Quarters != 0 && best.Score.Chars >= Math.Max(12, baseScore.Chars * 1.5))
                {
                    var redo = ImageAnalysis.Prepare(original.RotateQuarter(best.Quarters));
                    image = redo.Image;
                    applied = [$"rotação de {best.Quarters * 90}° (foto de lado ou invertida)", .. redo.Applied];
                    warnings = redo.Warnings.ToList();
                }
            }
        }

        var target = await WriteOutputAsync(imagePath, await EncodePngAsync(image));
        return new PreparedImage(target, applied, warnings);
    }

    /// <summary>Lê a imagem já na posição correta (respeita a rotação EXIF das câmeras de celular).</summary>
    public static async Task<GrayImage> DecodeAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = (long)decoder.PixelWidth * decoder.PixelHeight;
        var f = pixels > MaxDecodePixels ? Math.Sqrt((double)MaxDecodePixels / pixels) : 1.0;
        var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
        if (f < 1)
        {
            transform.ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * f));
            transform.ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * f));
        }

        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var bgra = data.DetachPixelData();
        var w = (int)Math.Max(1, Math.Round(decoder.OrientedPixelWidth * f));
        var h = (int)Math.Max(1, Math.Round(decoder.OrientedPixelHeight * f));
        if ((long)w * h * 4 != bgra.Length)
        {
            (w, h) = (h, w); // a escala foi aplicada antes da rotação EXIF
        }

        if ((long)w * h * 4 != bgra.Length)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)}: dimensões da imagem inconsistentes.");
        }

        return GrayImage.FromBgra(w, h, bgra);
    }

    public static async Task<byte[]> EncodePngAsync(GrayImage image)
    {
        using var mem = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, mem);
        // O codificador PNG do Windows não aceita Gray8: grava em BGRA com os mesmos tons de cinza.
        var bgra = new byte[image.Pixels.Length * 4];
        for (int i = 0, j = 0; i < image.Pixels.Length; i++, j += 4)
        {
            var v = image.Pixels[i];
            bgra[j] = v;
            bgra[j + 1] = v;
            bgra[j + 2] = v;
            bgra[j + 3] = 255;
        }

        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)image.Width, (uint)image.Height, 96, 96, bgra);
        await encoder.FlushAsync();
        var size = (uint)mem.Size;
        using var reader = new DataReader(mem.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    internal static SoftwareBitmap ToSoftwareBitmap(GrayImage image)
    {
        using var gray = SoftwareBitmap.CreateCopyFromBuffer(CryptographicBuffer.CreateFromByteArray(image.Pixels),
            BitmapPixelFormat.Gray8, image.Width, image.Height);
        return SoftwareBitmap.Convert(gray, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
