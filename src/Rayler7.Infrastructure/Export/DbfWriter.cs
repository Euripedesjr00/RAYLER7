using System.Text;

namespace Rayler7.Infrastructure.Export;

/// <summary>
/// dBASE III (.dbf) com todos os campos do tipo Caractere: preserva zeros à esquerda, vírgulas e casas decimais.
/// Codificação Windows-1252 (página 0x03), compatível com Excel, LibreOffice, QGIS e Sisvar.
/// </summary>
public static class DbfWriter
{
    public const int MaxFieldLength = 254;

    public static void Write(string path, ExportSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var names = Exporters.SafeNames(sheet.Headers, 10);
        var widths = new int[names.Count];
        for (var c = 0; c < names.Count; c++)
        {
            var w = sheet.Rows.Select(r => c < r.Count ? enc.GetByteCount(r[c]) : 0).DefaultIfEmpty(1).Max();
            if (w > MaxFieldLength)
            {
                throw new InvalidOperationException($"A coluna \"{sheet.Headers[c]}\" tem valores com mais de {MaxFieldLength} caracteres, limite do formato DBF. Use CSV ou XLSX.");
            }

            widths[c] = Math.Max(1, w);
        }

        var recordLength = 1 + widths.Sum();
        var headerLength = 32 + (32 * names.Count) + 1;
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        var today = DateTime.Today;
        bw.Write((byte)0x03);
        bw.Write((byte)(today.Year - 1900));
        bw.Write((byte)today.Month);
        bw.Write((byte)today.Day);
        bw.Write(sheet.Rows.Count);
        bw.Write((short)headerLength);
        bw.Write((short)recordLength);
        bw.Write(new byte[17]);
        bw.Write((byte)0x03); // Windows ANSI (1252)
        bw.Write(new byte[2]);
        for (var c = 0; c < names.Count; c++)
        {
            var nameBytes = new byte[11];
            Encoding.ASCII.GetBytes(names[c].ToUpperInvariant()).CopyTo(nameBytes, 0);
            bw.Write(nameBytes);
            bw.Write((byte)'C');
            bw.Write(new byte[4]);
            bw.Write((byte)widths[c]);
            bw.Write((byte)0);
            bw.Write(new byte[14]);
        }

        bw.Write((byte)0x0D);
        foreach (var row in sheet.Rows)
        {
            bw.Write((byte)' ');
            for (var c = 0; c < names.Count; c++)
            {
                byte[] bytes;
                try
                {
                    bytes = enc.GetBytes(c < row.Count ? row[c] : string.Empty);
                }
                catch (EncoderFallbackException)
                {
                    throw new InvalidOperationException($"Há caracteres que o formato DBF (Windows-1252) não representa na coluna \"{sheet.Headers[c]}\". Use CSV UTF-8 ou XLSX.");
                }

                var field = Enumerable.Repeat((byte)' ', widths[c]).ToArray();
                bytes.CopyTo(field, 0);
                bw.Write(field);
            }
        }

        bw.Write((byte)0x1A);
    }
}
