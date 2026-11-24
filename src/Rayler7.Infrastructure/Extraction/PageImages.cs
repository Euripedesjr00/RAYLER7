using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Rayler7.Infrastructure.Extraction;

/// <summary>
/// Converte páginas de PDF digitalizado e fotos HEIC em PNG usando componentes que já vêm no Windows
/// (gratuitos e locais). As imagens ficam ao lado da cópia do arquivo no projeto, para a conferência visual.
/// </summary>
public static class PageImages
{
    /// <summary>Imagem usada para recortes de conferência: a própria foto, ou a página renderizada.</summary>
    public static string? PreviewPath(string storedPath, int? page)
    {
        var ext = Path.GetExtension(storedPath);
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return page is { } p ? PdfPagePath(storedPath, p) : null;
        }

        return ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) ? HeicPngPath(storedPath) : storedPath;
    }

    public static string PdfPagePath(string pdfPath, int pageNumber) =>
        Path.Combine(pdfPath + "_paginas", $"pagina_{pageNumber:D4}.png");

    public static string HeicPngPath(string heicPath) => heicPath + ".png";

    /// <summary>Renderiza a página (1 = primeira) em cerca de 300 dpi.</summary>
    public static async Task<string> RenderPdfPageAsync(string pdfPath, int pageNumber)
    {
        var target = PdfPagePath(pdfPath, pageNumber);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
        var doc = await PdfDocument.LoadFromFileAsync(file);
        using var page = doc.GetPage((uint)(pageNumber - 1));
        // Size está em DIPs (96 por polegada): x3 ≈ 288 dpi, limitado para não estourar a memória.
        var width = (uint)Math.Clamp(page.Size.Width * 3, 800, 5000);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var outFile = await folder.CreateFileAsync(Path.GetFileName(target), CreationCollisionOption.ReplaceExisting);
        using (IRandomAccessStream stream = await outFile.OpenAsync(FileAccessMode.ReadWrite))
        {
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width });
        }

        return target;
    }

    /// <summary>Converte HEIC em PNG com o decodificador do Windows (extensão HEIF da Microsoft Store).</summary>
    public static async Task<string> ConvertHeicAsync(string heicPath)
    {
        var target = HeicPngPath(heicPath);
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(heicPath));
        SoftwareBitmap bitmap;
        try
        {
            using IRandomAccessStream input = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(input);
            bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            throw new NotSupportedException(
                "Este Windows não tem o decodificador de fotos HEIC. Instale \"Extensões de Imagem HEIF\" e \"Extensões de Vídeo HEVC\" " +
                "pela Microsoft Store, ou converta a foto para JPG no app Fotos e envie novamente.", ex);
        }

        using (bitmap)
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(Path.GetFullPath(target))!);
            var outFile = await folder.CreateFileAsync(Path.GetFileName(target), CreationCollisionOption.ReplaceExisting);
            using IRandomAccessStream output = await outFile.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
        }

        return target;
    }
}
