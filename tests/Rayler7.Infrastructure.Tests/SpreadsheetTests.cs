using ClosedXML.Excel;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.Infrastructure.Tests;

public class SpreadsheetTests
{
    private static string CreateTemplate(TempDir dir)
    {
        var path = dir.File("modelo.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Dados");
        ws.Cell(1, 1).Value = "Experimento de campo";
        ws.Range(1, 1, 1, 4).Merge();
        ws.Cell(2, 1).Value = "Tratamento";
        ws.Cell(2, 2).Value = "Repetição";
        ws.Cell(2, 3).Value = "Altura";
        ws.Cell(2, 4).Value = "Total";
        ws.Cell(3, 1).Value = "T1";
        ws.Cell(3, 4).FormulaA1 = "C3*2";
        ws.Range(2, 1, 10, 4).SetAutoFilter();
        wb.AddWorksheet("Notas");
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public void Analisa_abas_cabecalhos_formulas_e_mesclagens()
    {
        using var dir = new TempDir();
        var path = CreateTemplate(dir);

        var a = SpreadsheetAnalyzer.Analyze(path);

        Assert.Equal(2, a.Sheets.Count);
        var s = a.Sheets[0];
        Assert.Equal("Dados", s.Name);
        Assert.Equal(4, s.ColumnCount);
        Assert.Single(s.Formulas);
        Assert.Contains("C3*2", s.Formulas[0], StringComparison.Ordinal);
        Assert.Single(s.MergedRanges);
        Assert.True(s.HasAutoFilter);
        Assert.Equal(2, s.HeaderRow); // o título mesclado da linha 1 não é cabeçalho
        Assert.Equal("Tratamento", s.Headers[0]);
        Assert.Equal("=C3*2", s.Preview[2][3]);
        Assert.Equal(64, a.Sha256.Length);
    }

    [Fact]
    public void Preenche_copia_sem_tocar_no_modelo_e_preserva_texto()
    {
        using var dir = new TempDir();
        var template = CreateTemplate(dir);
        var output = dir.File("saida.xlsx");

        var report = TemplateFiller.Fill(template, output,
        [
            new CellWrite("Dados", 3, 2, "001"),
            new CellWrite("Dados", 3, 3, "12,50"),
            new CellWrite("Dados", 4, 3, "0.005"),
            new CellWrite("Dados", 3, 1, "T9"),   // já preenchida -> conflito
            new CellWrite("Dados", 3, 4, "99"),   // fórmula -> conflito
            new CellWrite("Dados", 5, 3, ""),     // vazio continua vazio
        ]);

        Assert.True(report.TemplateUnchanged);
        Assert.Equal(3, report.CellsWritten);
        Assert.Equal(2, report.Conflicts.Count);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet("Dados");
        Assert.Equal("001", ws.Cell(3, 2).GetString());
        Assert.Equal("12,50", ws.Cell(3, 3).GetString());
        Assert.Equal("0.005", ws.Cell(4, 3).GetString());
        Assert.Equal("T1", ws.Cell(3, 1).GetString());
        Assert.True(ws.Cell(3, 4).HasFormula);
        Assert.True(ws.Cell(5, 3).IsEmpty());
    }

    [Fact]
    public void Nao_grava_sobre_o_proprio_modelo_nem_sobre_arquivo_existente()
    {
        using var dir = new TempDir();
        var template = CreateTemplate(dir);

        Assert.Throws<InvalidOperationException>(() => TemplateFiller.Fill(template, template, []));
        System.IO.File.WriteAllText(dir.File("existe.xlsx"), "x");
        Assert.Throws<IOException>(() => TemplateFiller.Fill(template, dir.File("existe.xlsx"), []));
    }

    [Fact]
    public void Modo_numerico_autorizado_mantem_casas_decimais_no_formato()
    {
        using var dir = new TempDir();
        var template = CreateTemplate(dir);
        var output = dir.File("num.xlsx");

        TemplateFiller.Fill(template, output, [new CellWrite("Dados", 6, 3, "10,00"), new CellWrite("Dados", 6, 2, "007")], WriteMode.NumberWhenSafe);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet("Dados");
        Assert.Equal(10d, ws.Cell(6, 3).GetDouble());
        Assert.Equal("0.00", ws.Cell(6, 3).Style.NumberFormat.Format);
        Assert.Equal("007", ws.Cell(6, 2).GetString()); // código nunca vira número
    }

    [Fact]
    public void Csv_ida_e_volta_preserva_valores()
    {
        using var dir = new TempDir();
        var path = dir.File("dados.csv");
        IReadOnlyList<string>[] rows =
        [
            ["Código", "Valor", "Obs"],
            ["001", "12,50", "texto; com separador"],
            ["00025", "0.005", "aspas \"internas\""],
            ["", "-3", ""],
        ];

        CsvFile.Write(path, rows);
        var (read, delimiter, _) = CsvFile.Read(path);

        Assert.Equal(';', delimiter);
        Assert.Equal(rows.Length, read.Count);
        for (var i = 0; i < rows.Length; i++)
        {
            Assert.Equal(rows[i], read[i]);
        }
    }

    [Fact]
    public void Csv_latin1_e_virgula()
    {
        using var dir = new TempDir();
        var path = dir.File("antigo.csv");
        System.IO.File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes("Repetição,Altura\n1,\"12,5\"\n"));

        var (rows, delimiter, _) = CsvFile.Read(path);

        Assert.Equal(',', delimiter);
        Assert.Equal("Repetição", rows[0][0]);
        Assert.Equal("12,5", rows[1][1]);
    }

    [Fact]
    public void Formatos_nao_implementados_sao_recusados_com_mensagem_clara()
    {
        using var dir = new TempDir();
        var xls = dir.File("velho.xls");
        System.IO.File.WriteAllText(xls, "x");

        var ex = Assert.Throws<NotSupportedException>(() => SpreadsheetAnalyzer.Analyze(xls));
        Assert.Contains("ainda não está implementado", ex.Message, StringComparison.Ordinal);
    }
}
