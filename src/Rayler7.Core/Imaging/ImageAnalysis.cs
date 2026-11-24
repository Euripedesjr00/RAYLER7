namespace Rayler7.Core.Imaging;

public sealed record ImageStats(double Mean, int P01, int P05, int P95, double Sharpness, int Width, int Height)
{
    public int ContrastSpan => P95 - P05;

    public bool LowLight => Mean < 78;

    public bool LowContrast => ContrastSpan < 95;

    public bool Overexposed => Mean > 235 && ContrastSpan < 45;

    public bool Blurry => Sharpness > 0 && Sharpness < 6.5;
}

public sealed record PreparedGray(GrayImage Image, IReadOnlyList<string> Applied, IReadOnlyList<string> Warnings, double SkewCorrected);

/// <summary>
/// Prepara fotos de celular e digitalizações para o OCR, sem IA: corrige iluminação e contraste,
/// endireita a página inclinada, amplia imagens pequenas e realça texto desfocado.
/// Imagens limpas passam sem alteração de tons. O texto nunca é alterado — só a imagem.
/// </summary>
public static class ImageAnalysis
{
    /// <summary>Maior lado após o preparo: acima disso o OCR fica lento sem ganhar precisão.</summary>
    public const int MaxSide = 3600;

    /// <summary>Imagens com o maior lado abaixo disso são ampliadas (letras pequenas demais para o OCR).</summary>
    public const int MinSide = 1600;

    public static ImageStats Analyze(GrayImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var probe = image.Width > 260 || image.Height > 260
            ? image.Scale(260.0 / Math.Max(image.Width, image.Height))
            : image;
        var hist = new long[256];
        long sum = 0;
        foreach (var v in probe.Pixels)
        {
            hist[v]++;
            sum += v;
        }

        var n = probe.Pixels.Length;
        var (p01, p05, p95) = (Percentile(hist, n, 0.01), Percentile(hist, n, 0.05), Percentile(hist, n, 0.95));
        double grad = 0;
        var count = 0;
        for (var y = 1; y < probe.Height - 1; y++)
        {
            for (var x = 1; x < probe.Width - 1; x++)
            {
                grad += Math.Abs(probe[x + 1, y] - probe[x - 1, y]) + Math.Abs(probe[x, y + 1] - probe[x, y - 1]);
                count++;
            }
        }

        return new ImageStats((double)sum / n, p01, p05, p95, count > 0 ? grad / count : 0, image.Width, image.Height);
    }

    /// <summary>
    /// Tabela de tons que leva a tinta para o preto e o papel para o branco; null quando a imagem já
    /// está boa (papel claro e tinta escura) ou quando não há tinta a realçar (página em branco).
    /// </summary>
    public static byte[]? ContrastLut(ImageStats s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var ink = s.P01;
        var paper = s.P95;
        if (paper - ink < 40 || (paper >= 200 && ink <= 90 && s.Mean >= 85))
        {
            return null;
        }

        var low = Math.Max(0, ink - 4);
        var high = Math.Min(255, Math.Max(low + 40, paper + 4));
        var gamma = s.Mean < 85 ? 0.82 : s.Mean > 215 ? 1.12 : 1.0;
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var norm = Math.Clamp((i - low) / (double)(high - low), 0, 1);
            lut[i] = (byte)Math.Clamp(Math.Round(Math.Pow(norm, gamma) * 255), 0, 255);
        }

        return lut;
    }

    /// <summary>
    /// Inclinação das linhas de texto/tabela, em graus (até ±15°). Devolve o ângulo que, aplicado em
    /// <see cref="GrayImage.Rotate"/>, endireita a imagem; 0 quando já está reta ou é inconclusivo.
    /// Método: projeção horizontal dos pixels escuros — linhas retas concentram tinta em poucas faixas.
    /// </summary>
    public static double DetectSkew(GrayImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var probe = image.Width > 900 || image.Height > 900 ? image.Scale(900.0 / Math.Max(image.Width, image.Height)) : image;
        if (probe.Width < 40 || probe.Height < 40)
        {
            return 0;
        }

        long sum = 0;
        foreach (var v in probe.Pixels)
        {
            sum += v;
        }

        var mean = sum / (double)probe.Pixels.Length;
        var threshold = Math.Max(40, mean - 40);
        var xs = new List<float>();
        var ys = new List<float>();
        double cx = probe.Width / 2.0, cy = probe.Height / 2.0;
        for (var y = 1; y < probe.Height - 1; y++)
        {
            for (var x = 1; x < probe.Width - 1; x++)
            {
                if (probe[x, y] < threshold)
                {
                    xs.Add((float)(x - cx));
                    ys.Add((float)(y - cy));
                }
            }
        }

        if (xs.Count < 200 || xs.Count > probe.Pixels.Length * 0.6)
        {
            return 0; // pouca tinta, ou imagem quase toda escura: inconclusivo
        }

        var diag = (int)Math.Ceiling(Math.Sqrt((probe.Width * probe.Width) + (probe.Height * probe.Height)));
        double Variance(double deg)
        {
            var rad = deg * Math.PI / 180;
            var (sin, cos) = (Math.Sin(rad), Math.Cos(rad));
            var bins = new int[diag + 2];
            for (var i = 0; i < xs.Count; i++)
            {
                var yr = (sin * xs[i]) + (cos * ys[i]) + (diag / 2.0);
                var b = (int)yr;
                if (b >= 0 && b < bins.Length)
                {
                    bins[b]++;
                }
            }

            double m = xs.Count / (double)bins.Length, v = 0;
            foreach (var c in bins)
            {
                v += (c - m) * (c - m);
            }

            return v;
        }

        var best = 0.0;
        var bestVar = Variance(0);
        var zeroVar = bestVar;
        for (var a = -15.0; a <= 15.0; a += 0.5)
        {
            var v = Variance(a);
            if (v > bestVar)
            {
                (best, bestVar) = (a, v);
            }
        }

        for (var a = best - 0.45; a <= best + 0.45; a += 0.1)
        {
            var v = Variance(a);
            if (v > bestVar)
            {
                (best, bestVar) = (a, v);
            }
        }

        var improvement = zeroVar > 0 ? (bestVar - zeroVar) / zeroVar : 0;
        return Math.Abs(best) < 0.4 || improvement < 0.05 ? 0 : Math.Round(best, 1);
    }

    /// <summary>Aplica somente as correções necessárias e informa o que foi feito.</summary>
    public static PreparedGray Prepare(GrayImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var applied = new List<string>();
        var warnings = new List<string>();
        var img = image;
        var maxSide = Math.Max(img.Width, img.Height);
        if (maxSide > MaxSide)
        {
            img = img.Scale((double)MaxSide / maxSide);
            applied.Add($"redução para {img.Width}×{img.Height} px (leitura mais rápida)");
        }

        var stats = Analyze(img);
        if (stats.Overexposed)
        {
            warnings.Add("Possível reflexo ou foto estourada: confira os valores com atenção.");
        }

        if (ContrastLut(stats) is { } lut)
        {
            img = img.ApplyLut(lut);
            applied.Add(stats.LowLight ? "correção de pouca luz" : "realce de contraste");
        }

        var skew = DetectSkew(img);
        if (skew != 0)
        {
            img = img.Rotate(skew);
            applied.Add($"endireitamento de {Math.Abs(skew):0.#}°");
        }

        maxSide = Math.Max(img.Width, img.Height);
        if (maxSide < MinSide)
        {
            var factor = Math.Min(2.5, 2000.0 / maxSide);
            img = img.Scale(factor);
            applied.Add($"ampliação {factor:0.0}× (imagem pequena)");
            if (maxSide < 700)
            {
                warnings.Add($"Imagem de baixa resolução ({image.Width}×{image.Height} px): fotografe mais perto para melhor leitura.");
            }
        }

        if (stats.Blurry)
        {
            img = img.Sharpen(0.6);
            applied.Add("realce de nitidez");
            warnings.Add("Foto possivelmente desfocada: confira os valores com atenção.");
        }

        return new PreparedGray(img, applied, warnings, skew);
    }

    private static int Percentile(long[] hist, int n, double q)
    {
        var target = (long)(n * q);
        long acc = 0;
        for (var v = 0; v < 256; v++)
        {
            acc += hist[v];
            if (acc >= target && acc > 0)
            {
                return v;
            }
        }

        return 255;
    }
}
