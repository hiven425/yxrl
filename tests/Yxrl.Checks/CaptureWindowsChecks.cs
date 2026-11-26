using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
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
    private static CaptureOptions Global(string start = "", string clipboard = "") => new()
    {
        Shortcuts = new ShotShortcuts(Start: start, Clipboard: clipboard)
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
            var pick = new PixelPoint(current.X - capture.Bounds.Left, current.Y - capture.Bounds.Top);
            surface.SelectWindowAt(pick);
            Pump();
            Check(surface.Selection == selected, "正式选区窗口没有选中同一物理区域");
            overlay.Run(ImageAction.Pin);
            Pump();
            var pin = app.Windows.OfType<ShotWindow>().Single();
            Check(pin.Topmost && !pin.Selecting && Child<ShotSurface>(pin)!.Document.Width == selected.Width, "贴图像素或置顶状态不符");
            pin.Run(ImageAction.Ocr);
            Pump();
            var words = app.Windows.OfType<TextToolWindow>().Single();
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline && !All<TextBox>(words).Any(f => f.Text.Contains("会议")))
                Pump(80);
            Check(All<TextBox>(words).Any(f => f.Text.Contains("Room", StringComparison.OrdinalIgnoreCase)), "文字窗口没有显示真实识别结果");
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
