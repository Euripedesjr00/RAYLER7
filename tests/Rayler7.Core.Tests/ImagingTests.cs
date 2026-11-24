using Rayler7.Core.Imaging;

namespace Rayler7.Core.Tests;

public class ImagingTests
{
    /// <summary>Página sintética: papel claro com 8 linhas de "texto" escuro.</summary>
    private static GrayImage Page(byte paper = 245, byte ink = 20)
    {
        var img = GrayImage.Blank(800, 600, paper);
        for (var line = 0; line < 8; line++)
        {
            var y0 = 60 + (line * 60);
            for (var y = y0; y < y0 + 14; y++)
            {
                for (var x = 80; x < 720; x++)
                {
                    if ((x / 9) % 4 != 3)
                    {
                        img.Pixels[(y * 800) + x] = ink; // "palavras" separadas por espaços
                    }
                }
            }
        }

        return img;
    }

    [Fact]
    public void Pagina_reta_nao_e_girada()
    {
        Assert.Equal(0, ImageAnalysis.DetectSkew(Page()));
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(-6.5)]
    public void Pagina_inclinada_e_endireitada(double tilt)
    {
        var tilted = Page().Rotate(tilt);

        var correction = ImageAnalysis.DetectSkew(tilted);

        Assert.InRange(correction, -tilt - 0.6, -tilt + 0.6);
        Assert.Equal(0, ImageAnalysis.DetectSkew(tilted.Rotate(correction)));
    }

    [Fact]
    public void Pagina_limpa_nao_tem_tons_alterados()
    {
        var stats = ImageAnalysis.Analyze(Page());

        Assert.Null(ImageAnalysis.ContrastLut(stats));
    }

    [Fact]
    public void Foto_escura_tem_tinta_e_papel_separados()
    {
        var dark = Page(paper: 140, ink: 70);

        var prepared = ImageAnalysis.Prepare(dark);

        Assert.Contains(prepared.Applied, a => a.Contains("luz", StringComparison.Ordinal) || a.Contains("contraste", StringComparison.Ordinal));
        var stats = ImageAnalysis.Analyze(prepared.Image);
        Assert.True(stats.P95 >= 230, $"papel deveria clarear (p95={stats.P95})");
        Assert.True(stats.P01 <= 40, $"tinta deveria escurecer (p01={stats.P01})");
    }

    [Fact]
    public void Giro_de_90_graus_ida_e_volta_preserva_pixels()
    {
        var page = Page();

        var back = page.RotateQuarter(1).RotateQuarter(3);

        Assert.Equal(page.Pixels, back.Pixels);
        Assert.Equal((600, 800), (page.RotateQuarter(1).Width, page.RotateQuarter(1).Height));
    }

    [Fact]
    public void Imagem_pequena_e_ampliada_e_grande_e_reduzida()
    {
        Assert.True(Math.Max(ImageAnalysis.Prepare(Page()).Image.Width, 1) >= ImageAnalysis.MinSide);
        var huge = GrayImage.Blank(5000, 3000);
        Assert.Equal(ImageAnalysis.MaxSide, ImageAnalysis.Prepare(huge).Image.Width);
    }
}
