using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Yxrl;
using Yxrl.Capture;
using Yxrl.Desktop;
using Yxrl.Views;

internal static class CaptureWindowsChecks
{
    [DllImport("dwmapi")]
    private static extern int DwmFlush();
    [DllImport("user32")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
    private static CaptureOptions Global(string start = "", string clipboard = "", string restorePin = "") => new()
    {
        Shortcuts = new ShotShortcuts(Start: start, Clipboard: clipboard, RestorePin: restorePin)
    };
    private static void Check(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException(message);
    }

    private static void Pump(int ms = 150)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ms)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    public static void Verify(string directory, string[] args)
    {
        SetThreadDpiAwarenessContext(-4);
        using (var keys = new ShotHotkeys(_ =>
        {
        }))
        using (var rival = new ShotHotkeys(_ =>
        {
        }))
        using (var probe = new ShotHotkeys(_ =>
        {
        }))
        {
            const string a = "Ctrl+Alt+Shift+F19", b = "Ctrl+Alt+Shift+F20", occupied = "Ctrl+Alt+Shift+F21", free = "Ctrl+Alt+Shift+F22";
            keys.Apply(Global(a, b), true);
            rival.Apply(Global(occupied), true);
            try
            {
                keys.Apply(Global(free, occupied), true);
                throw new Exception("被占用的热键注册成功");
            }
            catch (ShotBindingException e)
            {
                Check(e.Action == ShotAction.Clipboard, "热键冲突未定位到动作");
            }

            probe.Apply(Global(free), true);
            try
            {
                probe.Apply(Global(a), true);
                throw new Exception("改键失败后丢失原绑定");
            }
            catch (ShotBindingException)
            {
            }

            keys.Apply(new CaptureOptions { Enabled = false }, true);
            probe.Apply(Global(a, b), true);
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = CalendarResources() };
        Theme.Apply();
        var fixture = new Window
        {
            Title = "YXRL 截图公开验收样例",
            Width = 390,
            Height = 122,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            Background = Brushes.White
        };
        var label = new TextBlock
        {
            Text = "项目会议 14:30\nRoom B   Design review",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            Foreground = Brushes.Black,
            FontSize = 26,
            Margin = new Thickness(12)
        };
        fixture.Content = label;
        var store = new SettingsStore(Path.Combine(directory, "ui"));
        int live = Array.IndexOf(args, "--translate-live");
        var service = live < 0 ? new TranslationOptions() : new TranslationOptions(args[live + 1], args[live + 2]);
        store.Save(store.Current with { Capture = Global(), Translation = service });
        var tools = new CaptureTools(store, () =>
        {
        });
        try
        {
            fixture.Show();
            Pump();
            nint hwnd = new WindowInteropHelper(fixture).Handle;
            var area = Native.MonitorOf(hwnd).Work;
            int w = Math.Min(545, area.Width), h = Math.Min(170, area.Height);
            int x = area.Center.X - w / 2, y = area.Center.Y - h / 2;
            Native.SetWindowPos(hwnd, Native.Topmost, x, y, w, h, Native.NoActivate);
            Pump();
            DwmFlush();
            bool liveScreen = args.Contains("--capture-windows");
            var image = CaptureChecks.TestImage();
            var capture = liveScreen ? DesktopFrame.Freeze() : new DesktopFrame(image, new PixelRect(x, y, x + image.PixelWidth, y + image.PixelHeight), [new PixelRect(x, y, x + image.PixelWidth, y + image.PixelHeight)]);
            var bounds = liveScreen ? Native.VisibleRect(hwnd).Intersect(capture.Bounds) : capture.Bounds;
            var current = bounds.Center;
            Check(bounds.HasArea, $"公开测试窗口没有可见边界，工作区 {area} 桌面 {capture.Bounds} 窗口 {Native.VisibleRect(hwnd)}");
            var relative = bounds.Offset(-capture.Bounds.Left, -capture.Bounds.Top);
            var selected = capture.WindowAt(new(current.X - capture.Bounds.Left, current.Y - capture.Bounds.Top));
            Check(relative == selected, "点选窗口与真实物理边界不一致");
            var raster = new ShotDocument(capture.Image).Crop(selected).Render();
            var text = tools.Ocr.ReadAsync(raster, CancellationToken.None).GetAwaiter().GetResult();
            if (!text.Contains("会议") || !text.Contains("Room", StringComparison.OrdinalIgnoreCase))
            {
                var sample = new byte[raster.PixelWidth * raster.PixelHeight * 4];
                raster.CopyPixels(sample, raster.PixelWidth * 4, 0);
                var dc = Native.GetDC(0);
                var rgb = Native.GetPixel(dc, bounds.Left + 4, bounds.Top + 4);
                Native.ReleaseDC(0, dc);
                throw new InvalidOperationException($"实际桌面图像识别失败，区域 {bounds.Width}×{bounds.Height}，屏幕测试点 {rgb:X8}，图像蓝通道范围 {sample.Where((v, i) => i % 4 == 0).Min()}..{sample.Where((v, i) => i % 4 == 0).Max()}，识别文字数 {text.Length}");
            }

            if (liveScreen)
                tools.Invoke(ShotAction.Start);
            else
                new ShotWindow(tools, new ShotDocument(capture.Image), capture).Show();
            Pump();
            var overlay = app.Windows.OfType<ShotWindow>().Single(window => window.Selecting);
            var surface = Child<ShotSurface>(overlay)!;
            Check(!InputMethod.GetIsInputMethodEnabled(surface), "选区画布应禁用输入法附着以免吞掉快捷键");
            var pick = new PixelPoint(current.X - capture.Bounds.Left, current.Y - capture.Bounds.Top);
            surface.SelectWindowAt(pick);
            Pump();
            Check(surface.Selection == selected, "正式选区窗口没有选中同一物理区域");
            overlay.Run(ImageAction.Pin);
            Pump();
            var pin = app.Windows.OfType<ShotWindow>().Single();
            Check(pin.Topmost && !pin.Selecting && Child<ShotSurface>(pin)!.Document.Width == selected.Width, "贴图像素或置顶状态不符");
            pin.SetPinOpacity(0.65);
            Check(Math.Abs(pin.PinOpacity - 0.65) < 0.01, "贴图透明度设置");
            pin.TogglePinLock();
            pin.TogglePinTopmost();
            pin.ToggleThumbnailMode();
            Check(pin.IsLocked && !pin.Topmost && pin.IsThumbnailMode && Child<ShotSurface>(pin)!.Viewport is not null, "贴图锁定/置顶/缩略图状态");
            pin.ToggleThumbnailMode();
            pin.TogglePinTopmost();
            pin.TogglePinLock();
            pin.Run(ImageAction.Ocr);
            Pump();
            var words = app.Windows.OfType<TextToolWindow>().Single();
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline && !All<TextBox>(words).Any(f => f.Text.Contains("会议")))
                Pump(80);
            Check(All<TextBox>(words).Any(f => f.Text.Contains("Room", StringComparison.OrdinalIgnoreCase)), "文字窗口没有显示真实识别结果");
            pin.Close();
            Pump();
            Check(tools.HasClosedPins && tools.RestoreLastPin(), "关闭贴图应可由 Ctrl+3 恢复");
            Pump();
            var restoredPin = app.Windows.OfType<ShotWindow>().Single();
            Check(Math.Abs(restoredPin.PinOpacity - 0.65) < 0.01, "恢复的贴图未保留透明度");
            if (!liveScreen)
            {
                restoredPin.Close();
                Pump();
            }
            else
                pin = restoredPin;
            if (live >= 0)
            {
                tools.Invoke(ShotAction.Translate);
                Pump();
                var manual = app.Windows.OfType<TextToolWindow>().Single(win => win != words);
                var input = All<TextBox>(manual).Single(f => AutomationProperties.GetName(f) == "原文");
                var output = All<TextBox>(manual).Single(f => AutomationProperties.GetName(f) == "译文");
                const string line = "The meeting starts at 2 PM.";
                input.Text = "9287\n" + line;
                input.Select(5, line.Length);
                var send = All<Button>(manual).Single(b => Equals(b.Content, "翻译"));
                var peer = UIElementAutomationPeer.CreatePeerForElement(send)!;
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                deadline = DateTime.UtcNow.AddSeconds(65);
                while (DateTime.UtcNow < deadline && output.Text.Length == 0)
                    Pump(80);
                Check(output.Text.Any(c => c is >= '\u3400' and <= '\u9fff') && !output.Text.Contains("9287"), "粘贴面板选段翻译失败：" + output.Text);
                Console.WriteLine("真实文字面板选段翻译：" + output.Text);
            }

            if (liveScreen && args.Contains("--recording"))
            {
                words.Close();
                pin.Close();
                VerifyRecording(tools, fixture, current, args);
                VerifyScrolling(tools, fixture, current);
                if (Array.IndexOf(args, "--recording-quick") is int quick && quick >= 0)
                    VerifyQuickRecording(tools, fixture, current, args[quick + 1]);
            }
            VerifyCalendarMenu(store, tools);
            string source = liveScreen ? "真实 GDI 桌面" : "公开样例像素";
            Console.WriteLine($"{source} / Windows 选区 / 置顶贴图 / OCR，{selected.Width}×{selected.Height} px，DPI {Native.GetDpiForWindow(hwnd)}。");
        }
        finally
        {
            tools.Dispose();
            fixture.Close();
            app.Shutdown();
        }
    }

    private static void VerifyRecording(CaptureTools tools, Window fixture, PixelPoint current, string[] args)
    {
        Check(RegionRecording.EnsureDisplay(), "真实录屏验收需要活动显示器");
        void Color(Brush background)
        {
            fixture.Background = background;
            Pump();
            DwmFlush();
        }
        Color(Brushes.OrangeRed);
        var controls = PrepareRecording(tools, current);
        bool audio = args.Contains("--recording-device-audio");
        if (audio)
            All<CheckBox>(controls).Single(button => Equals(button.Content, "系统声音")).IsChecked = true;
        Click(controls, "开始录制");
        using var tone = audio ? Tone() : null;
        using var player = tone is not null ? new SoundPlayer(tone) : null;
        if (audio)
        {
            Pump(250);
            player!.Play();
        }
        Pump(550);
        Click(controls, "暂停");
        Color(Brushes.Blue);
        Pump(500);
        Color(Brushes.LimeGreen);
        Click(controls, "继续");
        Pump(650);
        Click(controls, "停止录制");
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline && !Application.Current.Windows.OfType<RecordingPreview>().Any())
            Pump(100);
        Check(Application.Current.Windows.OfType<RecordingPreview>().Any(),
            "停止录制后回放窗口未出现，状态 " + string.Join(" / ", All<TextBlock>(controls).Select(block => block.Text)));
        var playback = Application.Current.Windows.OfType<RecordingPreview>().Single();
        var video = Child<MediaElement>(playback)!;
        while (DateTime.UtcNow < deadline && !video.NaturalDuration.HasTimeSpan)
            Pump(150);
        Check(video.NaturalDuration.HasTimeSpan && video.Source is not null, "本机回放未打开刚录制的 MP4");
        Check(video.NaturalVideoWidth > 0 && video.NaturalVideoHeight > 0, "回放未读取真实录制区域的尺寸");
        var clip = playback.SourceClip;
        string? audibleFile = null;
        if (audio)
        {
            Check(clip.Tracks is { System: not null, Microphone: null }, "系统声音应保留为单独轨道");
            audibleFile = Path.Combine(clip.Session!.Folder, "test-mix.mp4");
            RecordingExport.Save(clip, audibleFile, RecordingFormat.Mp4, TimeSpan.Zero, clip.Duration, CancellationToken.None);
            clip = clip with { Path = audibleFile, Tracks = null };
        }
        using (var foundation = new MediaFoundation())
        using (var readback = new ClipReader(clip, audio, 0))
        {
            var colors = new List<(byte B, byte G, byte R)>();
            int peak = 0;
            foreach (var frame in readback.Samples(audio, clip.Duration.Ticks))
            {
                if (frame.IsVideo)
                {
                    int pixel = (6 * clip.Width + 6) * 4;
                    colors.Add((frame.Data[pixel], frame.Data[pixel + 1], frame.Data[pixel + 2]));
                }
                else
                    for (int i = 0; i + 1 < frame.Data.Length; i += 2)
                        peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(frame.Data, i)));
            }
            Check(colors.Any(p => p.R > 200 && p.B < 30) && colors.Any(p => p.G > 160 && p.R < 80),
                "录制文件没有读回实际桌面的前后两种颜色");
            Check(colors.All(p => p.B < 160), "暂停期间的画面进入了录屏");
            if (audio)
            {
                Check(peak >= 200, "系统声音录制未读回公开测试音");
                Console.WriteLine($"通过：WASAPI 系统声音录到本机公开测试音并解码，采样峰值 {peak}。");
            }
        }
        int evidence = Array.IndexOf(args, "--recording-evidence");
        if (evidence >= 0)
        {
            var files = Directory.CreateDirectory(args[evidence + 1]);
            foreach (var format in Enum.GetValues<RecordingFormat>())
                RecordingExport.Save(clip, Path.Combine(files.FullName, $"live-region.{format.ToString().ToLowerInvariant()}"),
                    format, TimeSpan.Zero, clip.Duration, CancellationToken.None);
        }
        if (audibleFile is not null) File.Delete(audibleFile);
        Click(playback, "播放");
        Pump(250);
        Check(video.Position > TimeSpan.Zero, "回放未推进");
        playback.Shutdown(preserveRecording: false);
        Console.WriteLine($"通过：Windows 公开桌面实录 {clip.Width}×{clip.Height}，{clip.Duration.TotalSeconds:F2} 秒，读回录制前后画面，暂停画面未写入。");
    }

    private static RecordingWindow PrepareRecording(CaptureTools tools, PixelPoint current)
    {
        var desktop = DesktopFrame.Freeze().Bounds;
        tools.Invoke(ShotAction.Record);
        Pump();
        var overlay = Application.Current.Windows.OfType<ShotWindow>().Single(w => w.Selecting);
        Child<ShotSurface>(overlay)!.SelectWindowAt(new(current.X - desktop.Left, current.Y - desktop.Top));
        Pump();
        Click(overlay, "录制屏幕");
        return Application.Current.Windows.OfType<RecordingWindow>().Single();
    }

    private static void VerifyQuickRecording(CaptureTools tools, Window fixture, PixelPoint point, string path)
    {
        fixture.Content = null;
        fixture.Background = Brushes.OrangeRed;
        Pump();
        var area = Native.VisibleRect(new WindowInteropHelper(fixture).Handle);
        var controls = PrepareRecording(tools, point);
        All<ComboBox>(controls).Single(field => AutomationProperties.GetName(field) == "模式").SelectedIndex = 1;
        Console.WriteLine("快速录制正在打开保存录屏对话框。");
        Click(controls, "开始录制");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline &&
            !All<Button>(controls).Any(button => button.IsEnabled && Equals(button.Content, "停止录制"))) Pump();
        Pump(550);
        Click(controls, "停止录制");
        while (controls.IsVisible && DateTime.UtcNow < deadline) Pump();
        Check(File.Exists(path) && !controls.IsVisible && !Application.Current.Windows.OfType<RecordingPreview>().Any(),
            "快速录制未直接保存目标 MP4");
        using var media = new MediaFoundation();
        int width = area.Width & ~1, height = area.Height & ~1;
        using var reader = new ClipReader(new RecordedClip(path, width, height, 24, TimeSpan.FromSeconds(2), false), false, 0);
        var first = reader.Samples(false, TimeSpan.FromSeconds(2).Ticks).First();
        int pixel = (6 * width + 6) * 4;
        Check(first.Data[pixel + 2] > 200 && first.Data[pixel] < 30, "快速录制未读回实际桌面画面");
        Console.WriteLine($"通过：原生另存为、快速 MP4 直存和录制画面读回，{width}×{height}。");
    }

    private static MemoryStream Tone()
    {
        const int rate = 48000, samples = rate / 3;
        var wave = new MemoryStream();
        using (var writer = new BinaryWriter(wave, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + samples * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(samples * 2);
            for (int n = 0; n < samples; n++)
                writer.Write((short)(2600 * Math.Sin(2 * Math.PI * 480 * n / rate) * Math.Sin(Math.PI * n / samples)));
        }
        wave.Position = 0;
        return wave;
    }

    private static void VerifyScrolling(CaptureTools tools, Window fixture, PixelPoint center)
    {
        var hwnd = new WindowInteropHelper(fixture).Handle;
        var area = Native.VisibleRect(hwnd);
        int span = area.Height, width = area.Width, offset = span / 3;
        byte[] colors = new byte[width * 4 * (span + offset)];
        new Random(8).NextBytes(colors);
        var source = BitmapSource.Create(width, span + offset, 96, 96, PixelFormats.Bgr32, null, colors, width * 4);
        var image = new Image { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        fixture.Content = image;
        void Frame(int y)
        {
            image.Source = new CroppedBitmap(source, new Int32Rect(0, y, width, span));
            Pump(140);
            DwmFlush();
        }
        Frame(0);
        var scene = DesktopFrame.Freeze();
        tools.Invoke(ShotAction.Start);
        Pump();
        var overlay = Application.Current.Windows.OfType<ShotWindow>().Single(w => w.Selecting);
        Child<ShotSurface>(overlay)!.SelectWindowAt(new(center.X - scene.Bounds.Left, center.Y - scene.Bounds.Top));
        Pump();
        Click(overlay, "滚动长截图");
        var window = Application.Current.Windows.OfType<ScrollWindow>().Single();
        var ready = All<Button>(window).Single(b => Equals(b.Content, "完成长截图"));
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!ready.IsEnabled && DateTime.UtcNow < deadline) Pump();
        var auto = All<CheckBox>(window).Single(b => Equals(b.Content, "滚动后自动拼接"));
        var peer = UIElementAutomationPeer.CreatePeerForElement(auto)!;
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
        Pump();
        Frame(offset);
        var next = All<Button>(window).Single(b => Equals(b.Content, "拼接下一屏"));
        if (next.IsEnabled) Click(window, "拼接下一屏");
        while (!ready.IsEnabled && DateTime.UtcNow < deadline) Pump();
        Click(window, "完成长截图");
        while (DateTime.UtcNow < deadline && !Application.Current.Windows.OfType<ShotWindow>().Any()) Pump();
        var pin = Application.Current.Windows.OfType<ShotWindow>().Single();
        var document = Child<ShotSurface>(pin)!.Document;
        Check(document.Width == width && document.Height == span + offset,
            $"真实桌面长截图预期 {width} × {span + offset}，实际 {document.Width} × {document.Height}");
        pin.Close();
        Console.WriteLine($"通过：Windows 桌面长截图拼接为 {width}×{span + offset}，回到置顶贴图。");
    }

    private static void Click(DependencyObject window, string name)
    {
        var button = All<Button>(window).Single(b => b.IsVisible &&
            (Equals(b.Content, name) || AutomationProperties.GetName(b) == name));
        Check(button.IsEnabled, name + "不可用");
        var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
        Pump();
    }

    // Load the app's own styles without starting its calendar singleton or reading personal settings.
    private static ResourceDictionary CalendarResources()
    {
        using var stream = typeof(CaptureWindowsChecks).Assembly.GetManifestResourceStream("CalendarTheme.xaml")!;
        var app = XDocument.Load(stream).Root!;
        var ns = app.Name.Namespace;
        var dictionary = new XElement(ns + "ResourceDictionary", app.Attributes().Where(a => a.IsNamespaceDeclaration),
            app.Element(ns + "Application.Resources")!.Elements());
        using var reader = dictionary.CreateReader();
        return (ResourceDictionary)XamlReader.Load(reader);
    }

    private static void VerifyCalendarMenu(SettingsStore store, CaptureTools tools)
    {
        var calendar = new CalendarService(store);
        using var weather = new WeatherService(store.DirectoryPath);
        var settings = new SettingsWindow(store, calendar, weather, tools, () => { }, () => { }, () => { });
        var month = new CalendarWindow(store, calendar, weather, new ReminderStore(store.DirectoryPath),
            section => settings.Present(null, section), tools);
        try
        {
            month.Show();
            Pump();
            var entry = (Button)month.FindName("CaptureEntry");
            void Choose(string title)
            {
                entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var item = entry.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, title));
                Check(entry.ContextMenu.IsOpen && item.IsEnabled, title + "菜单入口不可用");
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Pump();
            }
            var previous = Application.Current.Windows.OfType<TextToolWindow>().ToHashSet();
            Choose("粘贴文字翻译");
            var manual = Application.Current.Windows.OfType<TextToolWindow>().Single(w => !previous.Contains(w) && w.Title == "YXRL 粘贴文字翻译");
            manual.Close();
            Choose("截图与翻译设置");
            var page = Child<CaptureSettingsPage>(settings)!;
            Check(page.IsVisible, "设置未定位到截图页");
            var stop = All<Button>(page).Single(b => AutomationProperties.GetName(b) == "停用提取文字");
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            All<CheckBox>(page).Single(c => Equals(c.Content, "启用截图工具")).IsChecked = false;
            ((Button)settings.Page.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var reopened = new SettingsStore(store.DirectoryPath);
            reopened.Load();
            Check(!reopened.Current.Capture.Enabled && reopened.Current.Capture.Shortcuts.Ocr == "", "设置未保存停用的快捷键或总开关");
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(entry.ContextMenu.Items.OfType<MenuItem>().All(i => i.IsEnabled == (i.Tag is null)), "总开关与月历菜单不一致");
            Console.WriteLine("通过：月历截图菜单、手动翻译入口、设置定位与停用后的文件读回。");
        }
        finally
        {
            if (settings.IsVisible) settings.Close();
            month.Finish();
        }
    }

    private static T? Child<T>(DependencyObject node)
        where T : DependencyObject => All<T>(node).FirstOrDefault();
    private static IEnumerable<T> All<T>(DependencyObject node)
        where T : DependencyObject
    {
        if (node is T value)
            yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in All<T>(VisualTreeHelper.GetChild(node, i)))
                yield return child;
    }
}
