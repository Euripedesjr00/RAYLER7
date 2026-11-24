using ClosedXML.Excel;
using Rayler7.Core.Extraction;
using Rayler7.Infrastructure.Extraction;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.Infrastructure.Tests;

/// <summary>Leitura de fichas em texto e Word (sem OCR).</summary>
public class FormFileTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static readonly string[] Template = ["Nome", "Placa", "Observação"];

    [Fact]
    public void Ficha_em_txt_nao_corta_valor_na_virgula()
    {
        var o = DocumentExtractor.Extract(Fixture("fichas.txt"), "fichas.txt");
        var r = RecordBuilder.Build(Assert.Single(o.Tables), Template, []);

        Assert.Equal(new[] { "Maria Souza", "João Lima" }, r.Records.Where(x => x.TargetColumn == "A").Select(x => x.Value.Original));
        Assert.Contains(r.Records, x => x.TargetColumn == "C" && x.Value.Original == "chuva, vento forte");
    }

    [Fact]
    public void Ficha_em_paragrafos_do_word_vira_linhas()
    {
        var o = DocumentExtractor.Extract(Fixture("fichas.docx"), "fichas.docx");
        var r = RecordBuilder.Build(Assert.Single(o.Tables), Template, []);

        Assert.Equal(new[] { "ABC-1234", "XYZ-9876" }, r.Records.Where(x => x.TargetColumn == "B").Select(x => x.Value.Original));
        Assert.Contains(r.Records, x => x.TargetColumn == "C" && x.Value.Original == "chuva, vento");
    }

    [Fact]
    public void Modelo_xls_vira_xlsx_com_texto_exato()
    {
        using var dir = new TempDir();
        var src = dir.File("dados_legado.xls");
        File.Copy(Fixture("dados_legado.xls"), src);

        var converted = TemplateConverter.Convert(src);
        var analysis = SpreadsheetAnalyzer.Analyze(converted.XlsxPath);

        var sheet = analysis.Sheets[0];
        Assert.Equal("Campo", sheet.Name);
        Assert.Equal(new[] { "Tratamento", "Repetição", "Altura (cm)", "Data" }, sheet.Headers);
        using var wb = new XLWorkbook(converted.XlsxPath);
        Assert.Equal("001", wb.Worksheet(1).Cell(2, 2).GetString());
        Assert.NotEmpty(converted.Notes);
    }
}

/// <summary>OCR real (Tesseract + Windows): roda só no computador, não no GitHub Actions.</summary>
[Trait("Category", "OcrLocal")]
public class PhotoProcessingTests
{
    private static string Copy(TempDir dir, string name)
    {
        var path = dir.File(name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), path);
        return path;
    }

    private static string AllText(ExtractionOutput o) => string.Join(' ', o.Tables.SelectMany(t => t.Rows).SelectMany(r => r).Select(c => c.Text));

    [Fact]
    public void Foto_de_celular_girada_pelo_exif_inclinada_e_escura_e_lida()
    {
        using var dir = new TempDir();
        var path = Copy(dir, "foto_celular_exif.jpg");

        var o = DocumentExtractor.Extract(path, "foto_celular_exif.jpg");

        var text = AllText(o);
        Assert.Contains("Tratamento", text, StringComparison.Ordinal);
        Assert.Contains("12,50", text, StringComparison.Ordinal);
        Assert.Contains("001", text, StringComparison.Ordinal);
        Assert.Contains(o.Warnings, w => w.Contains("endireitamento", StringComparison.Ordinal));
        Assert.NotNull(ImagePreprocessor.LatestOcrPath(path));

        // Mesmo cabeçalho do modelo: cada valor cai na coluna certa, e o título acima é ignorado.
        string[] template = ["Tratamento", "Repetição", "Altura (cm)", "Peso (kg)"];
        var r = RecordBuilder.Build(o.Tables[0], template, []);
        Assert.Contains(r.Records, x => x.TargetColumn == "C" && x.Value.Original == "12,50");
        Assert.Contains(r.Records, x => x.TargetColumn == "D" && x.Value.Original == "3,20");
        Assert.DoesNotContain(r.Records, x => x.Value.Original.Contains("Ensaio", StringComparison.Ordinal));
    }

    [Fact]
    public void Foto_tirada_de_lado_e_virada_antes_da_leitura()
    {
        using var dir = new TempDir();
        var path = Copy(dir, "foto_de_lado.png");

        var o = DocumentExtractor.Extract(path, "foto_de_lado.png");

        Assert.Contains(o.Warnings, w => w.Contains("rotação de", StringComparison.Ordinal));
        Assert.Contains("Tratamento", AllText(o), StringComparison.Ordinal);
    }

    [Fact]
    public void Ficha_fotografada_vira_linhas_da_planilha()
    {
        using var dir = new TempDir();
        var path = Copy(dir, "ficha_foto.png");

        var o = DocumentExtractor.Extract(path, "ficha_foto.png");
        var r = RecordBuilder.Build(o.Tables[0], ["Nome", "Placa", "Peso (kg)"], []);

        Assert.Equal(new[] { "ABC-1234", "XYZ-9876" }, r.Records.Where(x => x.TargetColumn == "B").Select(x => x.Value.Original));
        Assert.Contains(r.Records, x => x.TargetColumn == "C" && x.Value.Original == "72,5");
    }

    [Fact]
    public void Modelo_em_foto_vira_planilha_com_cabecalho_lido()
    {
        using var dir = new TempDir();
        var path = Copy(dir, "foto_celular_exif.jpg");

        var converted = TemplateConverter.Convert(path);
        var analysis = SpreadsheetAnalyzer.Analyze(converted.XlsxPath);

        Assert.Contains("Tratamento", analysis.Sheets[0].Headers);
        Assert.Contains("Altura (cm)", analysis.Sheets[0].Headers);
        Assert.Contains(converted.Notes, n => n.Contains("Confira", StringComparison.Ordinal));
    }
}
