using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Rayler7.Core.Audit;
using Rayler7.Core.Extraction;
using Rayler7.Infrastructure.Extraction;
using Rayler7.Infrastructure.Persistence;
using Rayler7.Infrastructure.Projects;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Rayler7.Infrastructure.Tests;

public class ExtractionTests
{
    [Fact]
    public void Excel_preserva_texto_exibido_com_formato()
    {
        using var dir = new TempDir();
        var path = dir.File("dados.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Campo");
            ws.Cell(1, 1).Value = "Código";
            ws.Cell(1, 2).Value = "Valor";
            ws.Cell(2, 1).Value = "001";
            ws.Cell(2, 2).Value = 10d;
            ws.Cell(2, 2).Style.NumberFormat.Format = "0.00";
            wb.SaveAs(path);
        }

        var output = DocumentExtractor.Extract(path, "dados.xlsx");

        var t = Assert.Single(output.Tables);
        Assert.Equal("001", t.Rows[1][0].Text);
        Assert.Matches(@"^10[.,]00$", t.Rows[1][1].Text); // casas decimais do formato preservadas
    }

    [Fact]
    public void Word_le_tabelas()
    {
        using var dir = new TempDir();
        var path = dir.File("relatorio.docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var table = new W.Table(
                Row("Tratamento", "Altura"),
                Row("T1", "12,50"),
                Row("T2", "0,005"));
            main.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("Resultados"))), table));
        }

        var output = DocumentExtractor.Extract(path, "relatorio.docx");

        var t = Assert.Single(output.Tables);
        Assert.Equal("12,50", t.Rows[1][1].Text);
        Assert.Equal("0,005", t.Rows[2][1].Text);

        static W.TableRow Row(params string[] cells) =>
            new(cells.Select(c => new W.TableCell(new W.Paragraph(new W.Run(new W.Text(c))))));
    }

    [Fact]
    public void Pdf_digital_reconstroi_linhas_e_colunas()
    {
        using var dir = new TempDir();
        var path = dir.File("tabela.pdf");
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        string[][] lines = [["Tratamento", "Altura"], ["T1", "12,50"], ["T2", "001"]];
        for (var i = 0; i < lines.Length; i++)
        {
            page.AddText(lines[i][0], 12, new PdfPoint(50, 700 - (i * 20)), font);
            page.AddText(lines[i][1], 12, new PdfPoint(250, 700 - (i * 20)), font);
        }

        System.IO.File.WriteAllBytes(path, builder.Build());

        var output = DocumentExtractor.Extract(path, "tabela.pdf");

        var t = Assert.Single(output.Tables);
        Assert.Equal(3, t.Rows.Count);
        Assert.Equal(["T1", "12,50"], t.Rows[1].Select(c => c.Text));
        Assert.Equal("001", t.Rows[2][1].Text);
        Assert.Equal(1, output.PagesAnalyzed);
    }

    [Theory]
    [InlineData("foto.jpg", SupportLevel.Supported)]
    [InlineData("foto.heic", SupportLevel.Pending)]
    [InlineData("velho.xls", SupportLevel.Pending)]
    [InlineData("dados.csv", SupportLevel.Supported)]
    [InlineData("doc.pdf", SupportLevel.Supported)]
    [InlineData("x.exe", SupportLevel.Unsupported)]
    public void Matriz_de_suporte_real(string file, SupportLevel level) =>
        Assert.Equal(level, DocumentExtractor.Support(file));

    [Fact]
    public void Registros_e_selecao_persistem_com_estado_de_revisao()
    {
        using var dir = new TempDir();
        var db = new Database(dir.File("r7.db"));
        db.Migrate();
        var p = new ProjectService(db, dir.File("projetos")).Create("X", "ana");
        var store = new RecordStore(db);
        var table = new SourceTable("f.csv", null, null, [[new SourceCell("T1"), new SourceCell("1,08")]], []);
        var recs = RecordBuilder.Build(table, ["Trat", "Alt"], []).Records;
        store.Append(p.Id, null, recs);
        recs[1].Correct("1,80", "ana", p.Id, new InMemoryAuditLog());
        store.UpdateState(recs[1]);
        store.SaveSelection(p.Id, new HashSet<Guid> { recs[0].Id });

        var loaded = store.Load(p.Id);

        Assert.Equal(2, loaded.Count);
        Assert.True(loaded[0].Selected);
        Assert.False(loaded[1].Selected);
        Assert.Equal("1,08", loaded[1].Record.Value.Original);
        Assert.Equal("1,80", loaded[1].Record.EffectiveText);
        Assert.Equal(RecordStatus.CorrectedManually, loaded[1].Record.Status);
    }
}

public class FillRegressionTests
{
    [Fact]
    public void Modelo_somente_leitura_ainda_gera_copia_gravavel()
    {
        using var dir = new TempDir();
        var template = dir.File("modelo.xlsx");
        using (var wb = new XLWorkbook())
        {
            wb.AddWorksheet("Dados").Cell(1, 1).Value = "Trat";
            wb.SaveAs(template);
        }

        System.IO.File.SetAttributes(template, FileAttributes.ReadOnly);
        var report = Rayler7.Infrastructure.Spreadsheets.TemplateFiller.Fill(template, dir.File("saida.xlsx"),
            [new Rayler7.Infrastructure.Spreadsheets.CellWrite("Dados", 2, 1, "T1")]);

        Assert.Equal(1, report.CellsWritten);
        Assert.True(report.TemplateUnchanged);
    }

    [Fact]
    public void Csv_preenchido_mantem_linhas_originais()
    {
        using var dir = new TempDir();
        var template = dir.File("modelo.csv");
        System.IO.File.WriteAllText(template, "Trat;Alt\nT0;5\n");

        var report = Rayler7.Infrastructure.Spreadsheets.CsvFiller.Fill(template, dir.File("saida.csv"),
            [new("CSV", 3, 1, "T1"), new("CSV", 3, 2, "12,50"), new("CSV", 2, 2, "9")]);

        Assert.Equal(2, report.CellsWritten);
        Assert.Single(report.Conflicts);
        var (rows, _, _) = Rayler7.Infrastructure.Spreadsheets.CsvFile.Read(dir.File("saida.csv"));
        Assert.Equal(["T0", "5"], rows[1]);
        Assert.Equal(["T1", "12,50"], rows[2]);
    }
}
