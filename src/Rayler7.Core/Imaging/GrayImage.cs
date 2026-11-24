namespace Rayler7.Core.Imaging;

/// <summary>Imagem em tons de cinza (0 = preto, 255 = branco) para preparar fotos antes do OCR.</summary>
public sealed class GrayImage
{
    public GrayImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != width * height)
        {
            throw new ArgumentException("Quantidade de pixels não confere com as dimensões.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte this[int x, int y] => Pixels[(y * Width) + x];

    public static GrayImage Blank(int width, int height, byte value = 255)
    {
        var px = new byte[width * height];
        Array.Fill(px, value);
        return new GrayImage(width, height, px);
    }

    /// <summary>Converte BGRA (formato do Windows) em cinza pela luminância.</summary>
    public static GrayImage FromBgra(int width, int height, byte[] bgra)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        var px = new byte[width * height];
        for (int i = 0, p = 0; p < px.Length; i += 4, p++)
        {
            // Pixels transparentes viram papel branco.
            var a = bgra[i + 3] / 255.0;
            var lum = (0.114 * bgra[i]) + (0.587 * bgra[i + 1]) + (0.299 * bgra[i + 2]);
            px[p] = (byte)Math.Clamp(Math.Round((lum * a) + (255 * (1 - a))), 0, 255);
        }

        return new GrayImage(width, height, px);
    }

    /// <summary>Redimensiona: média da área ao reduzir (sem serrilhado) e bilinear ao ampliar.</summary>
    public GrayImage Resize(int newWidth, int newHeight)
    {
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);
        if (newWidth == Width && newHeight == Height)
        {
            return this;
        }

        var dst = new byte[newWidth * newHeight];
        var sx = (double)Width / newWidth;
        var sy = (double)Height / newHeight;
        if (sx >= 1 && sy >= 1)
        {
            for (var y = 0; y < newHeight; y++)
            {
                var y0 = (int)(y * sy);
                var y1 = Math.Min(Height, Math.Max(y0 + 1, (int)((y + 1) * sy)));
                for (var x = 0; x < newWidth; x++)
                {
                    var x0 = (int)(x * sx);
                    var x1 = Math.Min(Width, Math.Max(x0 + 1, (int)((x + 1) * sx)));
                    long sum = 0;
                    for (var yy = y0; yy < y1; yy++)
                    {
                        var row = yy * Width;
                        for (var xx = x0; xx < x1; xx++)
                        {
                            sum += Pixels[row + xx];
                        }
                    }

                    dst[(y * newWidth) + x] = (byte)(sum / ((y1 - y0) * (x1 - x0)));
                }
            }
        }
        else
        {
            for (var y = 0; y < newHeight; y++)
            {
                var fy = Math.Max(0, ((y + 0.5) * sy) - 0.5);
                for (var x = 0; x < newWidth; x++)
                {
                    var fx = Math.Max(0, ((x + 0.5) * sx) - 0.5);
                    dst[(y * newWidth) + x] = Sample(fx, fy, 255);
                }
            }
        }

        return new GrayImage(newWidth, newHeight, dst);
    }

    public GrayImage Scale(double factor) => Resize((int)Math.Round(Width * factor), (int)Math.Round(Height * factor));

    /// <summary>
    /// Gira a imagem pelo ângulo (graus) ao redor do centro, aumentando a tela para não cortar os cantos.
    /// Áreas novas ficam brancas. <see cref="ImageAnalysis.DetectSkew"/> devolve o ângulo que endireita.
    /// </summary>
    public GrayImage Rotate(double degrees)
    {
        if (Math.Abs(degrees) < 0.01)
        {
            return this;
        }

        var rad = degrees * Math.PI / 180;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var nw = (int)Math.Ceiling((Math.Abs(Width * cos) + Math.Abs(Height * sin)) - 1e-6);
        var nh = (int)Math.Ceiling((Math.Abs(Width * sin) + Math.Abs(Height * cos)) - 1e-6);
        var dst = new byte[nw * nh];
        double cx = (Width - 1) / 2.0, cy = (Height - 1) / 2.0, ncx = (nw - 1) / 2.0, ncy = (nh - 1) / 2.0;
        for (var y = 0; y < nh; y++)
        {
            var dy = y - ncy;
            for (var x = 0; x < nw; x++)
            {
                var dx = x - ncx;
                // Mapeamento inverso: ponto de destino → origem (rotação por -ângulo).
                var srcX = (cos * dx) + (sin * dy) + cx;
                var srcY = (-sin * dx) + (cos * dy) + cy;
                dst[(y * nw) + x] = Sample(srcX, srcY, 255);
            }
        }

        return new GrayImage(nw, nh, dst);
    }

    /// <summary>Gira em múltiplos de 90° no sentido horário (para fotos de lado ou de cabeça para baixo).</summary>
    public GrayImage RotateQuarter(int quarters)
    {
        quarters = ((quarters % 4) + 4) % 4;
        if (quarters == 0)
        {
            return this;
        }

        var (nw, nh) = quarters == 2 ? (Width, Height) : (Height, Width);
        var dst = new byte[nw * nh];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var (tx, ty) = quarters switch
                {
                    1 => (Height - 1 - y, x),
                    2 => (Width - 1 - x, Height - 1 - y),
                    _ => (y, Width - 1 - x),
                };
                dst[(ty * nw) + tx] = Pixels[(y * Width) + x];
            }
        }

        return new GrayImage(nw, nh, dst);
    }

    public GrayImage ApplyLut(byte[] lut)
    {
        ArgumentNullException.ThrowIfNull(lut);
        var dst = new byte[Pixels.Length];
        for (var i = 0; i < dst.Length; i++)
        {
            dst[i] = lut[Pixels[i]];
        }

        return new GrayImage(Width, Height, dst);
    }

    /// <summary>Realce de bordas (máscara de nitidez 3×3) para texto levemente desfocado.</summary>
    public GrayImage Sharpen(double amount = 0.5)
    {
        var dst = (byte[])Pixels.Clone();
        for (var y = 1; y < Height - 1; y++)
        {
            for (var x = 1; x < Width - 1; x++)
            {
                var i = (y * Width) + x;
                var center = Pixels[i];
                var neighbors = (Pixels[i - 1] + Pixels[i + 1] + Pixels[i - Width] + Pixels[i + Width]) * 0.25;
                dst[i] = (byte)Math.Clamp(Math.Round(center + ((center - neighbors) * amount)), 0, 255);
            }
        }

        return new GrayImage(Width, Height, dst);
    }

    private byte Sample(double x, double y, byte outside)
    {
        if (x < -0.5 || y < -0.5 || x > Width - 0.5 || y > Height - 0.5)
        {
            return outside;
        }

        var x0 = Math.Clamp((int)Math.Floor(x), 0, Width - 1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, Height - 1);
        var x1 = Math.Min(x0 + 1, Width - 1);
        var y1 = Math.Min(y0 + 1, Height - 1);
        var fx = Math.Clamp(x - x0, 0, 1);
        var fy = Math.Clamp(y - y0, 0, 1);
        var top = (Pixels[(y0 * Width) + x0] * (1 - fx)) + (Pixels[(y0 * Width) + x1] * fx);
        var bottom = (Pixels[(y1 * Width) + x0] * (1 - fx)) + (Pixels[(y1 * Width) + x1] * fx);
        return (byte)Math.Clamp(Math.Round((top * (1 - fy)) + (bottom * fy)), 0, 255);
    }
}
