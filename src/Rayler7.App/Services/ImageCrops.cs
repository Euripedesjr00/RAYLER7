using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rayler7.Core.Extraction;

namespace Rayler7.App.Services;

/// <summary>Recorta da imagem original o trecho onde o valor foi lido, para conferência visual.</summary>
public static class ImageCrops
{
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp" };

    public static ImageSource? Crop(string? imagePath, CellBox? box)
    {
        if (imagePath is null || box is null || !File.Exists(imagePath) || !ImageExt.Contains(Path.GetExtension(imagePath)))
        {
            return null;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(imagePath);
            bmp.EndInit();
            bmp.Freeze();

            // Margem para mostrar o contexto ao redor do valor.
            var mx = Math.Max(box.Width, 40);
            var my = Math.Max(box.Height * 1.5, 20);
            var x = (int)Math.Max(0, box.X - mx);
            var y = (int)Math.Max(0, box.Y - my);
            var w = (int)Math.Min(bmp.PixelWidth - x, box.Width + (2 * mx));
            var h = (int)Math.Min(bmp.PixelHeight - y, box.Height + (2 * my));
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            var crop = new CroppedBitmap(bmp, new Int32Rect(x, y, w, h));
            crop.Freeze();
            return crop;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UriFormatException)
        {
            return null;
        }
    }
}
