using System.Text;

namespace Rayler7.Infrastructure.Spreadsheets;

/// <summary>
/// Leitor e gravador de CSV que trabalha só com texto: nenhum valor é convertido.
/// </summary>
public static class CsvFile
{
    private static readonly char[] Candidates = [';', ',', '\t', '|'];

    public static (IReadOnlyList<IReadOnlyList<string>> Rows, char Delimiter, Encoding Encoding) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (text, encoding) = Decode(bytes);
        var delimiter = DetectDelimiter(text);
        return (Parse(text, delimiter), delimiter, encoding);
    }

    internal static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
        }

        try
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return (strict.GetString(bytes), new UTF8Encoding(false));
        }
        catch (DecoderFallbackException)
        {
            // Arquivos antigos do Excel brasileiro costumam vir em Latin-1/Windows-1252.
            return (Encoding.Latin1.GetString(bytes), Encoding.Latin1);
        }
    }

    internal static char DetectDelimiter(string text)
    {
        var firstLines = text.Split('\n', 6).Take(5).Where(l => l.Length > 0).ToList();
        if (firstLines.Count == 0)
        {
            return ';';
        }

        // Escolhe o separador que aparece com contagem constante (e maior) fora de aspas.
        var best = ';';
        var bestScore = -1;
        foreach (var c in Candidates)
        {
            var counts = firstLines.Select(l => CountOutsideQuotes(l, c)).ToList();
            if (counts[0] == 0)
            {
                continue;
            }

            var consistent = counts.All(n => n == counts[0]);
            var score = (consistent ? 1000 : 0) + counts[0];
            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        return best;
    }

    private static int CountOutsideQuotes(string line, char c)
    {
        var inQuotes = false;
        var n = 0;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == c && !inQuotes)
            {
                n++;
            }
        }

        return n;
    }

    internal static List<IReadOnlyList<string>> Parse(string text, char delimiter)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(ch);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    public static void Write(string path, IEnumerable<IReadOnlyList<string>> rows, char delimiter = ';', bool utf8Bom = true)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendJoin(delimiter, row.Select(v => Quote(v, delimiter)));
            sb.Append("\r\n");
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(utf8Bom));
    }

    private static string Quote(string? value, char delimiter)
    {
        value ??= string.Empty;
        var needs = value.Contains(delimiter, StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
                    || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal);
        return needs ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
