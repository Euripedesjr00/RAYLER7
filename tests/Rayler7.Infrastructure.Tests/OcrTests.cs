using Rayler7.Core.Extraction;
using Rayler7.Infrastructure.Extraction;

namespace Rayler7.Infrastructure.Tests;

public class LayoutTableTests
{
    private static OcrWord W(string t, double x, double y, double? conf = null) =>
        new(t, x, y, t.Length * 10, 20, conf, conf is null ? null : t.Select(c => new CharConfidence(c, conf.Value)).ToList());

    [Fact]
    public void Agrupa_linhas_e_separa_celulas_por_espaco()
    {
        OcrWord[] words =
        [
            W("Tratamento", 10, 10), W("Altura", 300, 12),
            W("T1", 10, 52), W("12,50", 300, 50),
            W("Mesma", 10, 92), W("celula", 75, 92), W("0,005", 300, 90),
        ];

        var rows = LayoutTable.Build(words);

        Assert.Equal(3, rows.Count);
        Assert.Equal(["T1", "12,50"], rows[1].Select(c => c.Text));
        Assert.Equal("Mesma celula", rows[2][0].Text);
        Assert.NotNull(rows[1][1].Box);
    }

    [Fact]
    public void Leva_confianca_e_texto_do_segundo_motor()
    {
        var primary = new List<OcrWord> { W("1,08", 10, 10, 0.6) };
        var secondary = new List<OcrWord> { W("1,80", 11, 10) };

        var rows = LayoutTable.Build(primary, LayoutTable.MatchFrom(secondary));

        var cell = rows[0][0];
        Assert.Equal(0.6, cell.Confidence);
        Assert.Equal("1,80", cell.AlternateText);
        Assert.Equal(4, cell.Characters!.Count);
    }
}

/// <summary>Dependem do OCR do Windows e dos dados do Tesseract instalados no computador (rodam no PC, não na nuvem).</summary>
[Trait("Category", "OcrLocal")]
public class OcrIntegrationTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "tabela_impressa.png");

    [Fact]
    public async Task Ocr_do_windows_le_tabela_impressa()
    {
        Assert.True(WindowsOcr.IsAvailable, "OCR do Windows indisponível neste computador");

        var words = await WindowsOcr.RecognizeAsync(Fixture);
        var text = string.Join(' ', words.Select(w => w.Text));

        Assert.Contains("12,50", text, StringComparison.Ordinal);
        Assert.Contains("001", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Tesseract_le_tabela_impressa_com_confianca()
    {
        Assert.True(TesseractOcr.IsAvailable, "por.traineddata ausente");

        var words = TesseractOcr.Recognize(Fixture);

        Assert.Contains(words, w => w.Text == "12,50");
        Assert.All(words, w => Assert.NotNull(w.Confidence));
    }

    [Fact]
    public void Extrator_de_imagem_monta_linhas_e_colunas()
    {
        var output = DocumentExtractor.Extract(Fixture, "tabela_impressa.png");

        var rows = Assert.Single(output.Tables).Rows;
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(3, r.Count));
        Assert.Equal(1, output.ImagesAnalyzed);
        Assert.Equal(["001", "12,50"], rows[1].Skip(1).Select(c => c.Text)); // números lidos exatamente
    }

    /// <summary>
    /// Regra principal do OCR: qualquer leitura diferente do que está na imagem precisa virar dúvida.
    /// (No teste real, o Tesseract leu "T1" como "Tl" com 51% de confiança.)
    /// </summary>
    [Fact]
    public void Nenhum_erro_de_leitura_passa_sem_virar_duvida()
    {
        string[][] expected = [["Tratamento", "Repetição", "Altura"], ["T1", "001", "12,50"], ["T2", "002", "10,00"], ["T3", "003", "0,005"]];
        var table = Assert.Single(DocumentExtractor.Extract(Fixture, "tabela_impressa.png").Tables);

        var records = Rayler7.Core.Extraction.RecordBuilder.Build(table, ["Tratamento", "Repetição", "Altura"], []).Records;

        foreach (var r in records)
        {
            var truth = expected[r.Row!.Value - 1][Rayler7.Core.Mapping.ColumnLetters.ToIndex(r.Column!) - 1];
            if (r.Value.Original != truth)
            {
                Assert.True(r.HasOpenDoubt, $"Leitura errada sem dúvida: \"{r.Value.Original}\" (esperado \"{truth}\")");
            }
        }
    }
}
