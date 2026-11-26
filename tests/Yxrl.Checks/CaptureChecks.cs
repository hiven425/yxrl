using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yxrl;
using Yxrl.Capture;
using Yxrl.Desktop;

internal static class CaptureChecks
{
    private static void Check(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException("截图验收失败：" + message);
    }

    internal static async Task Run(string root, string[] args)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, "capture"));
        var store = new SettingsStore(dir.FullName);
        File.WriteAllText(store.FilePath, """{"schemaVersion":1,"clock":{"futureKey":1},"capture":{"futureOption":12}}""");
        store.Load();
        Check(store.ReadError is null && store.Current.Capture.Shortcuts.Start == "Ctrl+1", "旧配置默认值");
        var shortcuts = new ShotShortcuts(Start: "Ctrl+Alt+F9", Ocr: "");
        var changed = store.Current with
        {
            Capture = new CaptureOptions
            {
                Enabled = false,
                Shortcuts = shortcuts
            },
            Translation = new TranslationOptions("http://localhost:1234/v1", "example", "英文")
        };
        store.Save(changed);
        var reopened = new SettingsStore(dir.FullName);
        reopened.Load();
        Check(reopened.Current == changed && (int? )JsonNode.Parse(File.ReadAllText(store.FilePath))?["capture"]?["futureOption"] == 12, "关闭与改键读回、保留扩展字段");
        Check(!ShotChord.TryParse("Ctrl+Ctrl+1", out _) && !ShotChord.TryParse("A", out _) && ShotChord.TryParse("Ctrl+1", out var k) && k.ToString() == "Ctrl+1", "按键解析");
        try
        {
            new CaptureOptions
            {
                Shortcuts = new(Start: "Ctrl+2")
            }.Validate();
            throw new Exception("接受了重复按键");
        }
        catch (FormatException)
        {
        }

        await Task.Run(() => Sta(() => CheckImages(dir.FullName, args)));
        if (args.Contains("--capture-windows") || args.Contains("--capture-ui"))
            await Task.Run(() => Sta(() => CaptureWindowsChecks.Verify(dir.FullName, args)));
        await CheckTranslation();
        if (Array.IndexOf(args, "--translate-live") is int i && i >= 0)
        {
            var options = new TranslationOptions(args[i + 1], args[i + 2]);
            using var service = new TranslationService();
            var translated = await service.Translate(options, "The meeting starts at 2 PM.", "简体中文", null, CancellationToken.None);
            Check(translated.Any(c => c is >= '\u3400' and <= '\u9fff'), "真实模型没有返回中文");
            Console.WriteLine($"真实翻译 {options.Model}：The meeting starts at 2 PM. → {translated}");
        }

        Console.WriteLine("通过：截图设置兼容、裁剪、负坐标、标注与马赛克、剪贴板格式和透明度、PNG 回读、翻译接口与异常。");
    }

    private static void Sta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal static BitmapSource TestImage()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 560, 130));
            var type = new Typeface("Microsoft YaHei UI");
            for (int i = 0; i < 2; i++)
                dc.DrawText(new FormattedText(i == 0 ? "项目会议 14:30" : "Room B   Design review", CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, type, 30, Brushes.Black, 1), new Point(12, 6 + i * 58));
        }

        var bmp = new RenderTargetBitmap(560, 130, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static void CheckImages(string directory, string[] args)
    {
        CheckClipboardImage(directory);
        var image = TestImage();
        var doc = new ShotDocument(image);
        var frame = new DesktopFrame(image, new(-420, -10, 140, 120), [new(-370, 0, -120, 95)]);
        Check(frame.WindowAt(new(100, 30)) == new PixelRect(50, 10, 300, 105), "副屏负坐标窗口选区");
        var dual = frame with
        {
            Bounds = new(-1920, 0, 2560, 1440),
            Monitors = [new(-1920, 0, 0, 1080), new(0, 0, 2560, 1440)]
        };
        Check(dual.ToolsArea(new(1350, 920, 2050, 1100)) == new PixelRect(0, 0, 1920, 1080), "跨屏工具栏应在选区主要所在的可见屏幕");
        Check(ShotSelection.Move(new(10, 10, 100, 90), -200, 400, new(0, 0, 200, 200)) == new PixelRect(0, 120, 90, 200), "移动至屏幕边缘");
        doc.Add(new(MarkKind.Rectangle, [new(4, 3), new(185, 55)]));
        doc.Add(new(MarkKind.Mosaic, [new(12, 4), new(215, 54)]));
        doc.Undo();
        Check(doc.CanRedo, "撤销");
        doc.Redo();
        var crop = doc.Crop(new(5, 3, 330, 112));
        var surface = new ShotSurface(doc, frame);
        foreach (double factor in new[]
        {
            1.0,
            1.5
        })
        {
            var size = new Size(doc.Width / factor, doc.Height / factor);
            surface.Measure(size);
            surface.Arrange(new Rect(size));
            var view = surface.ViewRect(new(5, 3, 330, 112));
            Check(Math.Abs(view.Width * factor - 325) < .01 && Math.Abs(view.Height * factor - 109) < .01, "100%/150% 选区尺寸");
        }

        var pixels = new byte[4];
        crop.Render(true).CopyPixels(new Int32Rect(40, 22, 1, 1), pixels, 4, 0);
        Check(pixels.SequenceEqual(new byte[] { 0, 0, 0, 255 }), "马赛克的 OCR 输入仍包含像素");
        var file = Path.Combine(directory, "capture.png");
        CaptureTools.SavePng(crop, file);
        using (var stream = File.OpenRead(file))
        {
            var bmp = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Check(bmp.PixelWidth == 325 && bmp.PixelHeight == 109, "PNG 原始像素尺寸");
            new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(2, 0, 1, 1), pixels, 4, 0);
            Check(pixels.SequenceEqual(new byte[] { 47, 64, 212, 255 }), "导出的 PNG 缺少矩形标注");
        }

        if (!args.Contains("--ocr"))
            return;
        using var reader = new OcrReader(directory);
        var real = reader.ReadAsync(image, CancellationToken.None).GetAwaiter().GetResult();
        Check(real.Contains("会议") && real.Contains("Room", StringComparison.OrdinalIgnoreCase), "中英混排：" + real);
        Console.WriteLine("真实本地 OCR：" + real.Replace(Environment.NewLine, " / "));
        var sanitized = reader.ReadAsync(crop.Render(true), CancellationToken.None).GetAwaiter().GetResult();
        Check(!sanitized.Contains("会议") && sanitized.Contains("Room", StringComparison.OrdinalIgnoreCase), "马赛克识别隔离：" + sanitized);
        int arg = Array.IndexOf(args, "--ocr-image");
        if (arg >= 0)
        {
            using var stream = File.OpenRead(args[arg + 1]);
            var bmp = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            bmp.Freeze();
            var recognized = reader.ReadAsync(bmp, CancellationToken.None).GetAwaiter().GetResult();
            Check(recognized.Contains("screenshot", StringComparison.OrdinalIgnoreCase) && recognized.Contains("toolbar", StringComparison.OrdinalIgnoreCase), "示例英文识别");
            Console.WriteLine($"实图识别 {bmp.PixelWidth}×{bmp.PixelHeight}，命中 screenshot 和 toolbar，共 {recognized.Length} 字符。");
        }
    }

    private static void CheckClipboardImage(string directory)
    {
        byte[] expected = [41, 64, 160, 255, 41, 73, 161, 47, 0, 0, 0, 0];
        var original = BitmapSource.Create(3, 1, 96, 96, PixelFormats.Bgra32, null, expected, 12);
        var emptyBitmap = BitmapSource.Create(3, 1, 96, 96, PixelFormats.Bgra32, null, new byte[12], 12);
        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, emptyBitmap);
        Check(CaptureTools.ReadClipboardImage(data) == emptyBitmap, "纯位图剪贴板");

        BitmapSource image;
        using (var png = new MemoryStream())
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(original));
            encoder.Save(png);
            data.SetData("PNG", png, autoConvert: false);
            image = CaptureTools.ReadClipboardImage(data) ?? throw new Exception("混合格式剪贴板没有图片");
            Check(png.Position == png.Length, "读取图片改变了提供方的流位置");
        }
        var path = Path.Combine(directory, "clipboard-export.png");
        CaptureTools.SavePng(new ShotDocument(image), path);
        using var exported = File.OpenRead(path);
        var frame = BitmapFrame.Create(exported, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var pixels = new byte[expected.Length];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 12, 0);
        Check(pixels.SequenceEqual(expected), "剪贴板 PNG 导出丢失颜色或透明度");

        using var broken = new MemoryStream([1, 2, 3]);
        data.SetData("PNG", broken, autoConvert: false);
        Check(CaptureTools.ReadClipboardImage(data) == emptyBitmap, "PNG 损坏时没有读取同一份剪贴板里的位图");
        Check(CaptureTools.ReadClipboardImage(new DataObject()) is null, "空剪贴板产生了贴图");
    }

    private static async Task CheckTranslation()
    {
        var handler = new Stub();
        using var service = new TranslationService(handler);
        var options = new TranslationOptions("http://localhost:4321/custom/v1/", "demo");
        var result = await service.Translate(options, "Room B", "简体中文", "unit-test", CancellationToken.None);
        Check(result == "B 会议室" && handler.Path == "/custom/v1/chat/completions" && handler.Original == "Room B", "接口路径与选段");
        handler.Status = HttpStatusCode.Unauthorized;
        try
        {
            await service.Translate(options, "abc", "英文", null, CancellationToken.None);
            throw new Exception("鉴权错误被视为成功");
        }
        catch (HttpRequestException ex)
        {
            Check(ex.StatusCode == HttpStatusCode.Unauthorized, "鉴权错误类型");
        }

        handler.Status = HttpStatusCode.OK;
        handler.Body = """{"choices":[]}""";
        try
        {
            await service.Translate(options, "abc", "英文", null, CancellationToken.None);
            throw new Exception("空译文被视为成功");
        }
        catch (InvalidDataException)
        {
        }
    }

    private sealed class Stub : HttpMessageHandler
    {
        public string? Path, Original;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = """{"choices":[{"message":{"content":"B 会议室"}}]}""";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Path = request.RequestUri?.AbsolutePath;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = json.RootElement;
            Original = root.GetProperty("messages")[1].GetProperty("content").GetString();
            Check(root.GetProperty("model").GetString() == "demo" && root.GetProperty("messages")[1].GetProperty("content").ValueKind == JsonValueKind.String, "纯文字模型请求");
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json")
            };
        }
    }
}
