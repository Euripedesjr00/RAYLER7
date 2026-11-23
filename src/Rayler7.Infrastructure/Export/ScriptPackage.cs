using System.IO.Compression;
using System.Text;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.Infrastructure.Export;

/// <summary>Pacote CSV UTF-8 + script de importação que mantém tudo como texto (sem conversões silenciosas).</summary>
public static class ScriptPackage
{
    public static void Write(ExportFormat format, ExportSheet sheet, string zipPath)
    {
        var temp = Path.Combine(Path.GetTempPath(), "rayler7-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var names = Exporters.SafeNames(sheet.Headers, format == ExportFormat.PackageSas ? 32 : 30);
            var csv = Path.Combine(temp, "dados.csv");
            CsvFile.Write(csv, new[] { (IReadOnlyList<string>)names }.Concat(sheet.Rows), ';');
            var dict = new StringBuilder("variavel;cabecalho_original\r\n");
            for (var i = 0; i < names.Count; i++)
            {
                dict.Append(names[i]).Append(';').Append(sheet.Headers[i].Replace(";", ",", StringComparison.Ordinal)).Append("\r\n");
            }

            File.WriteAllText(Path.Combine(temp, "dicionario_de_dados.csv"), dict.ToString(), new UTF8Encoding(true));
            var (file, script) = format switch
            {
                ExportFormat.PackageR => ("importar.R", R()),
                ExportFormat.PackageSpss => ("importar.sps", Spss(names)),
                ExportFormat.PackageStata => ("importar.do", Stata()),
                _ => ("importar.sas", Sas(names, sheet)),
            };
            File.WriteAllText(Path.Combine(temp, file), script, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(temp, "LEIA-ME.txt"),
                "Gerado pelo RAYLER7 (Criado por Jhon).\r\nTodos os valores estão exatamente como na planilha, importados como TEXTO.\r\n" +
                "Converta para número dentro do seu programa estatístico, conferindo o separador decimal (vírgula).\r\n" +
                "dicionario_de_dados.csv relaciona o nome da variável ao cabeçalho original.\r\n", new UTF8Encoding(true));
            ZipFile.CreateFromDirectory(temp, zipPath);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static string R() =>
        "# RAYLER7 - importa tudo como texto, preservando zeros e virgulas\r\n" +
        "dados <- read.csv2(\"dados.csv\", fileEncoding = \"UTF-8-BOM\", colClasses = \"character\", na.strings = character(0))\r\n" +
        "str(dados)\r\n";

    private static string Spss(IReadOnlyList<string> names) =>
        "* RAYLER7 - importa tudo como texto.\r\nGET DATA /TYPE=TXT /FILE='dados.csv' /ENCODING='UTF8' /DELIMITERS=';' /QUALIFIER='\"'\r\n" +
        "  /ARRANGEMENT=DELIMITED /FIRSTCASE=2 /VARIABLES=\r\n" +
        string.Join("\r\n", names.Select(n => $"  {n} A255")) + ".\r\nEXECUTE.\r\n";

    private static string Stata() =>
        "* RAYLER7 - importa tudo como texto\r\nimport delimited using \"dados.csv\", delimiter(\";\") varnames(1) encoding(\"utf-8\") stringcols(_all) clear\r\ndescribe\r\n";

    private static string Sas(IReadOnlyList<string> names, ExportSheet sheet)
    {
        var len = Enumerable.Range(0, names.Count).Select(i => Math.Max(1, sheet.Rows.Select(r => i < r.Count ? r[i].Length : 0).DefaultIfEmpty(1).Max())).ToList();
        return "/* RAYLER7 - importa tudo como texto */\r\ndata dados;\r\n  infile 'dados.csv' dlm=';' dsd firstobs=2 encoding='utf-8' truncover;\r\n" +
               "  length " + string.Join(" ", names.Select((n, i) => $"{n} ${len[i]}")) + ";\r\n" +
               "  input " + string.Join(" ", names) + ";\r\nrun;\r\n";
    }
}
