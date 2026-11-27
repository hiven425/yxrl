using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed class ScrollWindow : Window
{
    private readonly PixelRect area;
    private readonly Action<BitmapSource> showPin;
    private readonly ComboBox axis = new();
    private readonly CheckBox automatic = new() { Content = "滚动后自动拼接", IsChecked = true };
    private readonly TextBlock status = new() { Text = "正在读取选区…", TextWrapping = TextWrapping.Wrap };
    private readonly Button next = new() { Content = "拼接下一屏", IsEnabled = false },
        finish = new() { Content = "完成长截图", IsEnabled = false };
    private readonly StackPanel strips = new();
    private readonly ScrollViewer preview = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(420) };
    private ScrollStitch stitch = new(horizontal: false);
    private bool positioned, busy, closed;

    public ScrollWindow(PixelRect area, Action<BitmapSource> showPin)
    {
        this.area = area;
        this.showPin = showPin;
        Title = "YXRL 长截图";
        if (Views.Theme.AppIcon is { } appIcon) Icon = appIcon;
        AutomationProperties.SetAutomationId(this, "YxrlScrollCapture");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Width = 316;
        Height = 332;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SetResourceReference(BackgroundProperty, "Surface");
        var body = new DockPanel { Margin = new Thickness(12) };
        Content = body;
        var toolbar = new StackPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        body.Children.Add(toolbar);
        toolbar.Children.Add(new TextBlock
        {
            Text = "在原窗口缓慢滚动，保留两屏重叠。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var options = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        toolbar.Children.Add(options);
        foreach (string text in new[] { "纵向向下", "横向向右" }) axis.Items.Add(text);
        axis.SelectedIndex = 0;
        axis.Margin = new Thickness(0, 0, 8, 0);
        AutomationProperties.SetName(axis, "滚动方向");
        options.Children.Add(axis);
        options.Children.Add(automatic);
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        toolbar.Children.Add(actions);
        next.Margin = new Thickness(0, 0, 6, 0);
        actions.Children.Add(next);
        actions.Children.Add(finish);
        toolbar.Children.Add(status);
        preview.Content = strips;
        preview.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetName(preview, "拼接预览");
        body.Children.Add(preview);
        next.Click += async (_, _) => await Read(manual: true);
        finish.Click += async (_, _) => await Complete();
        axis.SelectionChanged += async (_, _) =>
        {
            stitch = new(axis.SelectedIndex == 1);
            strips.Orientation = stitch.Horizontal ? Orientation.Horizontal : Orientation.Vertical;
            strips.Children.Clear();
            await Read(manual: true);
        };
        automatic.Click += (_, _) => timer.IsEnabled = positioned && automatic.IsChecked == true;
        timer.Tick += async (_, _) => await Read(manual: false);
        ContentRendered += async (_, _) =>
        {
            positioned = CaptureWindowPlacement.Place(this, area);
            if (!positioned)
            {
                status.Text = "请缩小选区，为长截图控制窗口留出空间。";
                return;
            }
            await Read(manual: true);
            if (!closed) timer.IsEnabled = automatic.IsChecked == true;
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => { closed = true; timer.Stop(); };
    }

    private async Task Read(bool manual)
    {
        if (busy || closed || !positioned)
            return;
        busy = true;
        next.IsEnabled = finish.IsEnabled = axis.IsEnabled = false;
        var current = stitch;
        try
        {
            var result = await Task.Run(() =>
            {
                if (DesktopFrame.SessionLocked() || !DesktopFrame.HasActiveDisplay())
                    throw new InvalidOperationException("Windows 桌面暂不可捕获。");
                using var pixels = new ScreenPixels(area);
                return current.Append(pixels.Image());
            });
            if (closed)
                return;
            if (result == StitchResult.Added)
            {
                var image = current.LastStrip!;
                double scale = Math.Min(1, 246.0 / (current.Horizontal ? image.PixelHeight : image.PixelWidth));
                strips.Children.Add(new Image { Source = image, Width = image.PixelWidth * scale,
                    Height = image.PixelHeight * scale, Stretch = Stretch.Fill });
                preview.UpdateLayout();
                if (current.Horizontal) preview.ScrollToRightEnd();
                else preview.ScrollToBottom();
                status.Text = $"{current.Width} × {current.Height} px";
            }
            else if (result == StitchResult.Limit)
            {
                status.Text = "已达到长截图像素上限。";
                timer.Stop();
            }
            else if (manual || result == StitchResult.NoMatch)
                status.Text = result == StitchResult.Unchanged ? "画面未变动，滚动后再试。" : "没有稳定的重叠区域。请返回上次位置附近，再缓慢滚动。";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            if (!closed) status.Text = ex.Message;
            timer.Stop();
        }
        finally
        {
            busy = false;
            next.IsEnabled = axis.IsEnabled = true;
            finish.IsEnabled = current.LastStrip is not null;
        }
    }

    private async Task Complete()
    {
        if (busy || stitch.LastStrip is null)
            return;
        timer.Stop();
        busy = true;
        next.IsEnabled = finish.IsEnabled = axis.IsEnabled = false;
        try
        {
            var result = await Task.Run(stitch.Finish);
            if (!closed)
            {
                showPin(result);
                Close();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            if (!closed)
            {
                status.Text = "无法生成长截图。 " + ex.Message;
                next.IsEnabled = finish.IsEnabled = axis.IsEnabled = true;
                busy = false;
            }
        }
    }
}
