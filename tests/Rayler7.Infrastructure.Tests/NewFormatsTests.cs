using System.Globalization;
using System.IO.Compression;
using Rayler7.Infrastructure.Export;
using Rayler7.Infrastructure.Extraction;

namespace Rayler7.Infrastructure.Tests;

public class NewFormatsTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static List<string> Row(ExtractionOutput o, int row) => o.Tables[0].Rows[row].Select(c => c.Text).ToList();

    [Fact]
    public void Xls_antigo_le_valores_como_o_excel_mostra()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            Assert.Equal(SupportLevel.Supported, DocumentExtractor.Support("a.xls"));
            var o = DocumentExtractor.Extract(Fixture("dados_legado.xls"), "dados_legado.xls");
            Assert.Equal("Campo", o.Tables[0].Label);
            Assert.Equal(new[] { "Tratamento", "Repetição", "Altura (cm)", "Data" }, Row(o, 0));
            Assert.Equal(new[] { "T1", "001", "12,50", "05/03/2026" }, Row(o, 1));
            Assert.Equal(new[] { "T2", "002", "8,00", "06/03/2026" }, Row(o, 2));
            Assert.Equal(new[] { "T3", "010", "10,25", "07/03/2026" }, Row(o, 3));
            Assert.Equal(4, o.Tables[0].Rows.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Ods_le_o_texto_exibido_sem_reformatar()
    {
        Assert.Equal(SupportLevel.Supported, DocumentExtractor.Support("a.ods"));
        var o = DocumentExtractor.Extract(Fixture("dados_planilha.ods"), "dados_planilha.ods");
        Assert.Single(o.Tables);
        Assert.Equal(new[] { "Tratamento", "Repetição", "Altura (cm)", "Data" }, Row(o, 0));
        // O arquivo de teste foi salvo com ponto decimal: o texto exibido é mantido como está.
        Assert.Equal(new[] { "T1", "001", "12.50", "05/03/2026" }, Row(o, 1));
        Assert.Equal(new[] { "T3", "010", "10.25", "07/03/2026" }, Row(o, 3));
        Assert.Equal(4, o.Tables[0].Rows.Count);
        Assert.All(o.Tables[0].Rows, r => Assert.Equal(4, r.Count)); // colunas repetidas vazias não viram 16 mil células
    }

    [Fact]
    public void Doc_e_rtf_continuam_pendentes_com_orientacao()
    {
        Assert.Equal(SupportLevel.Pending, DocumentExtractor.Support("a.doc"));
        Assert.Contains(".docx", DocumentExtractor.PendingReason("a.rtf"), StringComparison.Ordinal);
    }

    [Fact]
    public void Exporta_ods_com_todas_as_abas_e_valores_exatos()
    {
        using var dir = new TempDir();
        var path = dir.File("saida.ods");
        Exporters.Write(ExportFormat.Ods, [
            new ExportSheet("Dados", ["Código", "Altura"], [["001", "12,50"], ["002", "1.234"]]),
            new ExportSheet("Notas", ["Obs"], [["linha 1\nlinha 2"]]),
        ], path);

        // Lê de volta com o próprio leitor de .ods do programa.
        var back = LegacySpreadsheets.Ods(path, "saida.ods", CancellationToken.None);
        Assert.Equal(2, back.Tables.Count);
        Assert.Equal(new[] { "001", "12,50" }, back.Tables[0].Rows[1].Select(c => c.Text));
        Assert.Equal(new[] { "002", "1.234" }, back.Tables[0].Rows[2].Select(c => c.Text));
        Assert.Equal("linha 1\nlinha 2", back.Tables[1].Rows[1][0].Text);

        using var zip = ZipFile.OpenRead(path);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
    }
}

[Trait("Category", "OcrLocal")]
public class ScannedPdfTests
{
    [Fact]
    public void Pdf_digitalizado_e_lido_por_ocr_e_gera_imagem_da_pagina()
    {
        using var dir = new TempDir();
        var pdf = dir.File("tabela_digitalizada.pdf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tabela_digitalizada.pdf"), pdf);

        var o = DocumentExtractor.Extract(pdf, "tabela_digitalizada.pdf");

        Assert.Equal(1, o.ImagesAnalyzed);
        var table = Assert.Single(o.Tables);
        Assert.Equal(1, table.Page);
        Assert.Equal("Página 1 (OCR)", table.Label);
        var text = string.Join(' ', table.Rows.SelectMany(r => r).Select(c => c.Text));
        Assert.Contains("Tratamento", text, StringComparison.Ordinal);
        Assert.Contains("001", text, StringComparison.Ordinal);
        Assert.True(File.Exists(PageImages.PreviewPath(pdf, 1)), "imagem da página para conferência");
        // Células com caixa (posição) permitem mostrar o recorte da página na conferência.
        Assert.Contains(table.Rows.SelectMany(r => r), c => c.Box is not null);
    }
}
