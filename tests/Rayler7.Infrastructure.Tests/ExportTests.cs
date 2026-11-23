using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using Rayler7.Infrastructure.Export;

namespace Rayler7.Infrastructure.Tests;

public class ExportTests
{
    private static readonly ExportSheet Sheet = new("Dados", ["Tratamento", "Repetição", "Altura (cm)"],
        [["T1", "001", "12,50"], ["T2", "002", "0,005"], ["T3", "003", ""]]);

    private static string Filled(TempDir dir)
    {
        var path = dir.File("preenchida.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Dados");
        ws.Cell(1, 1).Value = "Ensaio";
        ws.Range(1, 1, 1, 3).Merge();
        ws.Cell(2, 1).Value = "Tratamento";
        ws.Cell(2, 2).Value = "Repetição";
        ws.Cell(2, 3).Value = "Altura (cm)";
        ws.Cell(3, 1).Value = "T1";
        ws.Cell(3, 2).Value = "001";
        ws.Cell(3, 3).Value = "12,50";
        ws.Cell(4, 1).Value = "T2";
        ws.Cell(4, 2).Value = "002";
        ws.Cell(4, 3).FormulaA1 = "LEN(B4)";
        wb.SaveAs(path);
        return path;
    }

    [Theory]
    [InlineData(StylePreset.Cientifico)]
    [InlineData(StylePreset.Academico)]
    [InlineData(StylePreset.Original)]
    public void Formatacao_nao_altera_nenhum_valor(StylePreset preset)
    {
        using var dir = new TempDir();
        var filled = Filled(dir);

        var report = XlsxStyler.Apply(filled, dir.File("estilo.xlsx"), TableStyle.For(preset));

        Assert.True(report.ValuesIdentical);
        Assert.True(report.CellsChecked >= 9);
        using var wb = new XLWorkbook(report.OutputPath);
        Assert.Equal("001", wb.Worksheet("Dados").Cell(3, 2).GetString());
        Assert.True(wb.Worksheet("Dados").Cell(4, 3).HasFormula);
    }

    [Fact]
    public void Leitura_para_exportar_ignora_titulo_e_preserva_texto()
    {
        using var dir = new TempDir();

        var sheets = ExportTable.Read(Filled(dir));

        var s = Assert.Single(sheets);
        Assert.Equal(["Tratamento", "Repetição", "Altura (cm)"], s.Headers);
        Assert.Equal(["T1", "001", "12,50"], s.Rows[0]);
    }

    [Fact]
    public void Csv_virgula_coloca_aspas_e_preserva()
    {
        using var dir = new TempDir();
        var path = dir.File("d.csv");

        Exporters.Write(ExportFormat.CsvUtf8Comma, [Sheet], path);

        var text = File.ReadAllText(path);
        Assert.Contains("T1,001,\"12,50\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Dbf_valido_com_campos_texto()
    {
        using var dir = new TempDir();
        var path = dir.File("d.dbf");

        Exporters.Write(ExportFormat.Dbf, [Sheet], path);

        var b = File.ReadAllBytes(path);
        Assert.Equal(0x03, b[0]);
        Assert.Equal(3, BitConverter.ToInt32(b, 4)); // registros
        var headerLen = BitConverter.ToInt16(b, 8);
        Assert.Equal(32 + (32 * 3) + 1, headerLen);
        Assert.Equal((byte)'C', b[32 + 11]);
        Assert.Equal("TRATAMENTO", Encoding.ASCII.GetString(b, 32, 10));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var body = Encoding.GetEncoding(1252).GetString(b, headerLen, b.Length - headerLen);
        Assert.Contains("001", body, StringComparison.Ordinal);
        Assert.Contains("12,50", body, StringComparison.Ordinal);
        Assert.Equal(0x1A, b[^1]);
    }

    [Theory]
    [InlineData(ExportFormat.PackageR, "importar.R")]
    [InlineData(ExportFormat.PackageSpss, "importar.sps")]
    [InlineData(ExportFormat.PackageStata, "importar.do")]
    [InlineData(ExportFormat.PackageSas, "importar.sas")]
    public void Pacotes_estatisticos_contem_csv_script_e_dicionario(ExportFormat format, string script)
    {
        using var dir = new TempDir();
        var path = dir.File("p.zip");

        Exporters.Write(format, [Sheet], path);

        using var zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.Name).ToList();
        Assert.Contains("dados.csv", names);
        Assert.Contains("dicionario_de_dados.csv", names);
        Assert.Contains(script, names);
        using var reader = new StreamReader(zip.GetEntry("dados.csv")!.Open());
        var csv = reader.ReadToEnd();
        Assert.Contains("T1;001;12,50", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Nomes_de_variaveis_seguros_e_unicos()
    {
        var n = Exporters.SafeNames(["Altura (cm)", "Altura (mm)", "1ª leitura", "", "Repetição"], 10);

        Assert.Equal("altura_cm", n[0]);
        Assert.Equal("altura_mm", n[1]);
        Assert.StartsWith("v", n[2], StringComparison.Ordinal);
        Assert.Equal("repeticao", n[4]);
        Assert.Equal(n.Count, n.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(n, x => Assert.True(x.Length <= 10));
    }

    [Fact]
    public void Nao_sobrescreve_arquivo_existente()
    {
        using var dir = new TempDir();
        var path = dir.File("x.csv");
        File.WriteAllText(path, "existente");

        Assert.Throws<IOException>(() => Exporters.Write(ExportFormat.CsvUtf8Semicolon, [Sheet], path));
        Assert.Equal("existente", File.ReadAllText(path));
    }
}
