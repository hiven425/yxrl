using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Yxrl;
using Yxrl.Capture;

internal static class RecognitionChecks
{
    public static void Verify(string root)
    {
        var path = Path.Combine(root, "recognition-settings");
        Directory.CreateDirectory(path);
        var settings = new SettingsStore(path);
        File.WriteAllText(settings.FilePath, """{"capture":{"shortcuts":{"ocr":"Shift+Q"}}}""");
        settings.Load();
        Check(settings.ReadError is null && settings.Current.Capture.Shortcuts.Ocr == "Shift+Q" &&
            settings.Current.Capture.Shortcuts.Table == "" && settings.Current.Capture.Shortcuts.Document == "Shift+E", "旧快捷键保留");
        Check(ShotColor.TryParse("#08F", out var color) && color.Format(0) == "#0088FF" &&
            ShotColor.TryParse(color.Format(1), out var rgb) && rgb == color &&
            ShotColor.TryParse("hsl(120, 100%, 50%)", out var hsl) && hsl.Format(0) == "#00FF00" &&
            ShotColor.TryParse("hsv(240, 100%, 100%)", out var hsv) && hsv.Format(0) == "#0000FF" &&
            !ShotColor.TryParse("样例 #08F", out _) && !ShotColor.TryParse("rgb(300,0,0)", out _), "颜色贴图区分完整色值与普通文字");

        var sheet = Path.Combine(root, "table.xlsx");
        RecognitionExport.SaveTable(sheet, new(3, 2,
        [
            new(0, 0, 1, 2, "合并表头"), new(1, 0, 1, 1, "0012"), new(1, 1, 1, 1, ""),
            new(2, 0, 1, 1, "=1+2"), new(2, 1, 1, 1, "已校对")
        ]));
        using var spreadsheet = ZipFile.OpenRead(sheet);
        var xml = Read(spreadsheet, "xl/worksheets/sheet1.xml");
        var ns = xml.Root!.Name.Namespace;
        var cells = xml.Descendants(ns + "c").ToDictionary(c => (string)c.Attribute("r")!);
        Check((string?)xml.Descendants(ns + "mergeCell").Single().Attribute("ref") == "A1:B1" &&
            cells["A2"].Value == "0012" && cells["B2"].Value == "" &&
            (string?)cells["A3"].Attribute("t") == "inlineStr" && cells["B3"].Value == "已校对", "Excel 合并、空格、原值和校对读回");

        var document = Path.Combine(root, "document.docx");
        RecognitionExport.SaveDocument(document, [new("项目说明", true), new("已校对的正文"), new("", Image: CaptureChecks.TestImage())]);
        using var word = ZipFile.OpenRead(document);
        var page = Read(word, "word/document.xml");
        ns = page.Root!.Name.Namespace;
        Check(page.Descendants(ns + "t").Select(x => x.Value).SequenceEqual(["项目说明", "已校对的正文"]) &&
            page.Descendants(ns + "outlineLvl").Count() == 1 && word.GetEntry("word/media/figure-1.png") is { Length: > 0 },
            "Word 标题、可编辑正文和配图读回");
        Console.WriteLine("通过：高级识别旧快捷键、颜色值、Word 和 Excel 真实文件读回。");
    }

    private static XDocument Read(ZipArchive archive, string name)
    {
        using var part = archive.GetEntry(name)!.Open();
        return XDocument.Load(part);
    }

    public static void Live(string folder, string documentSample, bool download)
    {
        Directory.CreateDirectory(folder);
        var models = new RecognitionModels(folder);
        using var ocr = new OcrReader(folder);
        string[] Files(RecognitionKind kind)
        {
            var files = models.Prepare(kind, download, null, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"模型就绪 {kind} {models.DownloadSize(kind):N0} 字节");
            return files;
        }

        BitmapSource page = BitmapFrame.Create(new Uri(Path.GetFullPath(documentSample)));
        page.Freeze();
        var blocks = StructureRecognition.ReadDocument(Files(RecognitionKind.Document)[0], page,
            ocr.ReadDocumentAsync(page, CancellationToken.None).GetAwaiter().GetResult(), CancellationToken.None);
        Check(blocks.Count(b => b.Text.Length > 0) >= 3 && blocks.Any(b => b.Image is not null), "版面模型识别正文和配图");
        Console.WriteLine($"文档识别 {blocks.Count(b => b.Text.Length > 0)} 文本块，{blocks.Count(b => b.Image is not null)} 配图");
        RecognitionExport.SaveDocument(Path.Combine(folder, "识别文档.docx"), blocks);

        var tableImage = Draw(520, 241, dc =>
        {
            Text(dc, "设备清单", 24, 208, 8, "Microsoft YaHei UI");
            var pen = new Pen(Brushes.Black, 2);
            foreach (int y in new[] { 4, 50, 96, 142, 188, 236 })
                dc.DrawLine(pen, new Point(6, y), new Point(514, y));
            foreach (int x in new[] { 6, 166, 344, 514 })
                dc.DrawLine(pen, new Point(x, x is 6 or 514 ? 4 : 50), new Point(x, 236));
            string[][] rows = [["编号", "品名", "备注"], ["0012", "钢笔", ""], ["0013", "墨水", "补充"], ["0014", "笔记本", "仓库"]];
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    Text(dc, rows[r][c], 23, 24 + c * 170, 56 + r * 46, "Microsoft YaHei UI");
        });
        var table = StructureRecognition.ReadTable(Files(RecognitionKind.Table)[0], tableImage,
            ocr.ReadDocumentAsync(tableImage, CancellationToken.None).GetAwaiter().GetResult(), CancellationToken.None);
        Console.WriteLine($"表格识别 {table.Rows}×{table.Columns}{Environment.NewLine}{table.ToTsv()}");
        Check(table is { Rows: 5, Columns: 3 } && table.Cells[0].Columns == 3 &&
            table.Cells.Any(c => c.Text == "0012") && table.Cells.Any(c => c.Row == 2 && c.Column == 2 && c.Text == ""), "表格模型保留合并表头、空格位与前导零");
        RecognitionExport.SaveTable(Path.Combine(folder, "识别表格.xlsx"), table);
        SaveSample(folder, "table.png", tableImage);

        var formulaFiles = Files(RecognitionKind.Formula);
        (string Name, string Expect, BitmapSource Image)[] formulas =
        [
            ("power", "^", Draw(130, 80, dc => { Text(dc, "𝑥", 53, 22, 8); Text(dc, "2", 27, 52, 3); })),
            ("fraction", "\\frac", Draw(148, 120, dc =>
            {
                Text(dc, "𝑎 + 𝑏", 32, 42, 10);
                dc.DrawLine(new Pen(Brushes.Black, 2), new Point(35, 54), new Point(126, 54));
                Text(dc, "𝑐", 32, 69, 60);
            })),
            ("radical", "\\sqrt", Draw(168, 81, dc =>
            {
                Text(dc, "√𝑥", 46, 33, 10);
                dc.DrawLine(new Pen(Brushes.Black, 2), new Point(65, 22), new Point(108, 22));
            })),
            ("sum", "\\sum", Draw(180, 120, dc =>
            {
                Text(dc, "𝑛", 22, 51, 1);
                Text(dc, "∑", 48, 35, 26);
                Text(dc, "𝑖=1", 21, 42, 85);
                Text(dc, "𝑖", 45, 90, 35);
            })),
            ("integral", "\\int", Draw(146, 110, dc =>
            {
                Text(dc, "∫", 55, 30, 16);
                Text(dc, "1", 20, 59, 4);
                Text(dc, "0", 20, 43, 78);
                Text(dc, "𝑥 d𝑥", 28, 66, 34);
            }))
        ];
        foreach (var (name, expected, fixture) in formulas)
        {
            SaveSample(folder, name + ".png", fixture);
            string latex = StructureRecognition.ReadFormula(formulaFiles, fixture, CancellationToken.None);
            Console.WriteLine($"{name} → {latex}");
            Check(latex.Contains(expected, StringComparison.Ordinal), name + " 公式结构");
        }
    }

    private static void SaveSample(string folder, string name, BitmapSource sample)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sample));
        using var file = File.Create(Path.Combine(folder, name));
        encoder.Save(file);
    }

    private static BitmapSource Draw(int width, int height, Action<DrawingContext> paint)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            paint(dc);
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static void Text(DrawingContext dc, string content, double size, double x, double y, string font = "Cambria Math") =>
        dc.DrawText(new FormattedText(content, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(font), size, Brushes.Black, 1), new Point(x, y));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("识别验收失败：" + message);
    }
}
