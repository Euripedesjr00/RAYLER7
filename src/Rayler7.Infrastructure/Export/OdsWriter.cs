using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Rayler7.Infrastructure.Export;

/// <summary>
/// Planilha OpenDocument (.ods) para LibreOffice. Todas as células são gravadas como texto,
/// exatamente como na planilha preenchida (zeros à esquerda e vírgulas decimais preservados).
/// </summary>
public static class OdsWriter
{
    private const string Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    public static void Write(string path, IReadOnlyList<ExportSheet> sheets)
    {
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        // O "mimetype" precisa ser o primeiro arquivo e sem compressão.
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mime.Open())
        {
            var b = Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.spreadsheet");
            s.Write(b, 0, b.Length);
        }

        WriteXml(zip, "META-INF/manifest.xml", w =>
        {
            const string m = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
            w.WriteStartElement("manifest", "manifest", m);
            w.WriteAttributeString("manifest", "version", m, "1.2");
            Entry(w, m, "/", "application/vnd.oasis.opendocument.spreadsheet");
            Entry(w, m, "content.xml", "text/xml");
            w.WriteEndElement();
        });

        WriteXml(zip, "content.xml", w =>
        {
            w.WriteStartElement("office", "document-content", Office);
            w.WriteAttributeString("xmlns", "table", null, Table);
            w.WriteAttributeString("xmlns", "text", null, Text);
            w.WriteAttributeString("xmlns", "style", null, Style);
            w.WriteAttributeString("xmlns", "fo", null, Fo);
            w.WriteAttributeString("office", "version", Office, "1.2");

            w.WriteStartElement("office", "automatic-styles", Office);
            w.WriteStartElement("style", "style", Style);
            w.WriteAttributeString("style", "name", Style, "cabecalho");
            w.WriteAttributeString("style", "family", Style, "table-cell");
            w.WriteStartElement("style", "text-properties", Style);
            w.WriteAttributeString("fo", "font-weight", Fo, "bold");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("office", "body", Office);
            w.WriteStartElement("office", "spreadsheet", Office);
            foreach (var sheet in sheets)
            {
                w.WriteStartElement("table", "table", Table);
                w.WriteAttributeString("table", "name", Table, sheet.Name);
                Row(w, sheet.Headers, header: true);
                foreach (var r in sheet.Rows)
                {
                    Row(w, r, header: false);
                }

                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        });
    }

    private static void Row(XmlWriter w, IReadOnlyList<string> cells, bool header)
    {
        w.WriteStartElement("table", "table-row", Table);
        foreach (var value in cells)
        {
            w.WriteStartElement("table", "table-cell", Table);
            if (header)
            {
                w.WriteAttributeString("table", "style-name", Table, "cabecalho");
            }

            if (!string.IsNullOrEmpty(value))
            {
                w.WriteAttributeString("office", "value-type", Office, "string");
                foreach (var line in value.Split('\n'))
                {
                    w.WriteElementString("text", "p", Text, line);
                }
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private static void Entry(XmlWriter w, string ns, string path, string type)
    {
        w.WriteStartElement("manifest", "file-entry", ns);
        w.WriteAttributeString("manifest", "full-path", ns, path);
        w.WriteAttributeString("manifest", "media-type", ns, type);
        w.WriteEndElement();
    }

    private static void WriteXml(ZipArchive zip, string name, Action<XmlWriter> body)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var w = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        w.WriteStartDocument();
        body(w);
        w.WriteEndDocument();
    }
}
