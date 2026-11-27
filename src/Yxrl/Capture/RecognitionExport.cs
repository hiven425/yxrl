using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace Yxrl.Capture;

internal static class RecognitionExport
{
    private const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private static readonly XNamespace Links = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = Office.TrimEnd('/');
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Picture = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    public static void SaveDocument(string path, IReadOnlyList<DocumentBlock> blocks) => Save(path, archive =>
    {
        string documentPath = "word/document.xml";
        Write(archive, "[Content_Types].xml", Types(
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
            Part(documentPath, "wordprocessingml.document.main+xml")));
        Write(archive, "_rels/.rels", Relationships(Rel("rId1", "officeDocument", documentPath)));
        var links = new List<XElement>();
        var body = new XElement(W + "body");
        foreach (var block in blocks)
        {
            if (block.Image is { } image)
            {
                int index = links.Count + 1;
                string media = $"media/figure-{index}.png";
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(image));
                using (var buffer = new MemoryStream())
                {
                    png.Save(buffer);
                    buffer.Position = 0;
                    using var file = archive.CreateEntry("word/" + media, CompressionLevel.NoCompression).Open();
                    buffer.CopyTo(file);
                }
                links.Add(Rel($"rId{index}", "image", media));
                body.Add(ImageParagraph(image, index));
            }
            else
            {
                foreach (string paragraph in block.Text.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.None))
                {
                    var run = new XElement(W + "r");
                    if (block.Heading)
                        run.Add(new XElement(W + "rPr", new XElement(W + "b"), new XElement(W + "sz", new XAttribute(W + "val", "28"))));
                    bool next = false;
                    foreach (var line in paragraph.Split('\n'))
                    {
                        if (next) run.Add(new XElement(W + "br"));
                        next = true;
                        run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), CleanText(line)));
                    }
                    body.Add(new XElement(W + "p", block.Heading
                        ? new XElement(W + "pPr", new XElement(W + "outlineLvl", new XAttribute(W + "val", "0")))
                        : null, run));
                }
            }
        }
        if (body.IsEmpty) body.Add(new XElement(W + "p"));
        Write(archive, documentPath, new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName), body));
        if (links.Count > 0)
            Write(archive, "word/_rels/document.xml.rels", Relationships(links.ToArray()));
    });

    private static XElement ImageParagraph(BitmapSource image, int index)
    {
        long cx = (long)Math.Min(5_715_000, image.PixelWidth * 9525.0);
        long cy = Math.Max(1, cx * image.PixelHeight / image.PixelWidth);
        string name = $"配图 {index}";
        var picture = new XElement(Picture + "pic",
            new XElement(Picture + "nvPicPr", new XElement(Picture + "cNvPr", new XAttribute("id", 0), new XAttribute("name", name)),
                new XElement(Picture + "cNvPicPr")),
            new XElement(Picture + "blipFill",
                new XElement(Drawing + "blip", new XAttribute(R + "embed", $"rId{index}")),
                new XElement(Drawing + "stretch", new XElement(Drawing + "fillRect"))),
            new XElement(Picture + "spPr",
                new XElement(Drawing + "xfrm",
                    new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(Drawing + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                new XElement(Drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(Drawing + "avLst"))));
        var inline = new XElement(WP + "inline", new XElement(WP + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
            new XElement(WP + "docPr", new XAttribute("id", index), new XAttribute("name", name)),
            new XElement(Drawing + "graphic", new XElement(Drawing + "graphicData", new XAttribute("uri", Picture.NamespaceName), picture)));
        return new XElement(W + "p", new XElement(W + "r", new XElement(W + "drawing", inline)));
    }

    public static void SaveTable(string path, RecognizedTable result) => Save(path, archive =>
    {
        const string workbook = "xl/workbook.xml", sheet = "xl/worksheets/sheet1.xml";
        Write(archive, "[Content_Types].xml", Types(Part(workbook, "spreadsheetml.sheet.main+xml"), Part(sheet, "spreadsheetml.worksheet+xml")));
        Write(archive, "_rels/.rels", Relationships(Rel("rId1", "officeDocument", workbook)));
        Write(archive, "xl/_rels/workbook.xml.rels", Relationships(Rel("rId1", "worksheet", "worksheets/sheet1.xml")));
        Write(archive, workbook, new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XElement(S + "sheets", new XElement(S + "sheet", new XAttribute("name", "识别结果"), new XAttribute("sheetId", 1),
                new XAttribute(R + "id", "rId1")))));

        var byRow = result.Cells.ToLookup(c => c.Row);
        var rows = new XElement(S + "sheetData", Enumerable.Range(0, result.Rows).Select(row =>
            new XElement(S + "row", new XAttribute("r", row + 1), byRow[row].OrderBy(cell => cell.Column).Select(cell =>
                // Inline strings keep leading zeros and preserve formula-looking OCR text as text.
                new XElement(S + "c", new XAttribute("r", Address(row, cell.Column)), new XAttribute("t", "inlineStr"),
                    new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), CleanText(cell.Text))))))));
        var merges = result.Cells.Where(cell => cell.Rows > 1 || cell.Columns > 1).Select(cell =>
            new XElement(S + "mergeCell", new XAttribute("ref", Address(cell.Row, cell.Column) + ":" +
                Address(cell.Row + cell.Rows - 1, cell.Column + cell.Columns - 1)))).ToArray();
        Write(archive, sheet, new XElement(S + "worksheet",
            new XElement(S + "dimension", new XAttribute("ref", "A1:" + Address(result.Rows - 1, result.Columns - 1))), rows,
            merges.Length == 0 ? null : new XElement(S + "mergeCells", new XAttribute("count", merges.Length), merges)));
    });

    private static string Address(int row, int column)
    {
        string label = "";
        for (int next = column + 1; next > 0; next = (next - 1) / 26)
            label = (char)('A' + (next - 1) % 26) + label;
        return label + (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CleanText(string value) =>
        string.Concat(value.EnumerateRunes().Where(r => r.Value > char.MaxValue || XmlConvert.IsXmlChar((char)r.Value)).Select(r => r.ToString()));
    private static XElement Relationships(params XElement[] entries) => new(Links + "Relationships", entries);
    private static XElement Rel(string id, string type, string to) => new(Links + "Relationship",
        new XAttribute("Id", id), new XAttribute("Type", Office + type), new XAttribute("Target", to));
    private static XElement Types(params XElement[] parts) => new(ContentTypes + "Types",
        new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
        new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")), parts);
    private static XElement Part(string path, string type) => new(ContentTypes + "Override",
        new XAttribute("PartName", "/" + path), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument." + type));

    private static void Write(ZipArchive archive, string name, XElement value)
    {
        using var output = archive.CreateEntry(name).Open();
        new XDocument(new XDeclaration("1.0", "utf-8", null), value).Save(output, SaveOptions.DisableFormatting);
    }

    private static void Save(string path, Action<ZipArchive> write)
    {
        string stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(stage, ZipArchiveMode.Create))
                write(archive);
            File.Move(stage, path, true);
        }
        finally
        {
            File.Delete(stage);
        }
    }
}
