namespace Rayler7.Infrastructure.Spreadsheets;

/// <summary>Preenche uma cópia de um modelo CSV: linhas originais intactas + novas linhas ao final.</summary>
public static class CsvFiller
{
    public static FillReport Fill(string templatePath, string outputPath, IEnumerable<CellWrite> writes)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (File.Exists(outputPath))
        {
            throw new IOException($"Já existe um arquivo em {outputPath}.");
        }

        var before = FileHash.Sha256(templatePath);
        var (rows, delimiter, _) = CsvFile.Read(templatePath);
        var table = rows.Select(r => r.ToList()).ToList();
        var conflicts = new List<FillConflict>();
        var written = 0;
        foreach (var w in writes)
        {
            while (table.Count < w.Row)
            {
                table.Add([]);
            }

            var row = table[w.Row - 1];
            while (row.Count < w.Column)
            {
                row.Add(string.Empty);
            }

            if (row[w.Column - 1].Length > 0)
            {
                conflicts.Add(new FillConflict(w.Sheet, w.Row, w.Column, row[w.Column - 1], w.Text));
                continue;
            }

            row[w.Column - 1] = w.Text;
            written++;
        }

        CsvFile.Write(outputPath, table.Select(r => (IReadOnlyList<string>)r), delimiter);
        return new FillReport(outputPath, written, conflicts, [], before, FileHash.Sha256(templatePath));
    }
}
