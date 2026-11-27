using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Yxrl.Desktop;
using Yxrl.Views;

namespace Yxrl.Capture;

internal sealed class ScrollWindow : Window
{
    private readonly PixelRect area;
    private readonly Action<BitmapSource> showPin;
    private readonly ComboBox axis = new();
    private readonly CheckBox automatic = new() { Content = "滚动后自动拼接", IsChecked = true };
    private readonly TextBlock status = new() { Text = "正在读取选区…", TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse statusDot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
    private readonly TextBlock countBadge = new() { FontSize = 11, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border countPill = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1.5, 8, 1.5), Visibility = Visibility.Collapsed };
    private readonly Button next = new() { Content = "拼接下一屏", IsEnabled = false },
        finish = new() { Content = "完成长截图", IsEnabled = false };
    private readonly StackPanel strips = new();
    private readonly ScrollViewer preview = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(420) };
    private ScrollStitch stitch = new(horizontal: false);
    private bool positioned, busy, closed, micaEnabled;
    private int stripCount;

    public ScrollWindow(PixelRect area, Action<BitmapSource> showPin)
    {
        this.area = area;
        this.showPin = showPin;
        Title = "YXRL 长截图";
        if (Theme.AppIcon is { } appIcon) Icon = appIcon;
        AutomationProperties.SetAutomationId(this, "YxrlScrollCapture");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Width = 348;
        Height = 384;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Ink");

        var body = new DockPanel { Margin = new Thickness(12) };
        Content = body;

        // Top header + options card
        var topStack = new StackPanel();
        var topHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        countBadge.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Typography.SetNumeralAlignment(countBadge, FontNumeralAlignment.Tabular);
        countPill.SetResourceReference(Border.BackgroundProperty, "Card");
        countPill.Child = countBadge;
        DockPanel.SetDock(countPill, Dock.Right);
        topHead.Children.Add(countPill);
        var hint = new TextBlock { Text = "在原窗口缓慢滚动，保留两屏重叠", FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        topHead.Children.Add(hint);
        topStack.Children.Add(topHead);

        var options = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (string text in new[] { "纵向向下", "横向向右" }) axis.Items.Add(text);
        axis.SelectedIndex = 0;
        axis.MinHeight = 28;
        axis.Padding = new Thickness(8, 3, 24, 3);
        axis.Margin = new Thickness(0, 0, 10, 0);
        AutomationProperties.SetName(axis, "滚动方向");
        options.Children.Add(axis);
        automatic.VerticalAlignment = VerticalAlignment.Center;
        options.Children.Add(automatic);
        topStack.Children.Add(options);

        var topCard = new Border { Margin = new Thickness(0, 0, 0, 9), Child = topStack };
        topCard.SetResourceReference(StyleProperty, "GroupCard");
        topCard.Padding = new Thickness(11, 9, 11, 9);
        DockPanel.SetDock(topCard, Dock.Top);
        body.Children.Add(topCard);

        // Bottom actions & semantic status
        var bottomStack = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        var actions = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
        next.SetResourceReference(StyleProperty, "SecondaryButton");
        next.MinHeight = 30;
        next.Margin = new Thickness(0, 0, 5, 0);
        finish.SetResourceReference(StyleProperty, "PrimaryButton");
        finish.MinHeight = 30;
        finish.Margin = new Thickness(5, 0, 0, 0);
        actions.Children.Add(next);
        actions.Children.Add(finish);
        bottomStack.Children.Add(actions);

        var statusRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(statusDot, Dock.Left);
        statusDot.SetResourceReference(Shape.FillProperty, "Accent");
        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        statusRow.Children.Add(statusDot);
        statusRow.Children.Add(status);
        bottomStack.Children.Add(statusRow);
        DockPanel.SetDock(bottomStack, Dock.Bottom);
        body.Children.Add(bottomStack);

        // Center framed preview stage
        AutomationProperties.SetName(preview, "拼接预览");
        var previewFrame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Child = preview
        };
        previewFrame.SetResourceReference(Border.BackgroundProperty, "Card");
        previewFrame.SetResourceReference(Border.BorderBrushProperty, "Line");
        preview.Content = strips;
        body.Children.Add(previewFrame);

        next.Click += async (_, _) => await Read(manual: true);
        finish.Click += async (_, _) => await Complete();
        axis.SelectionChanged += async (_, _) =>
        {
            stitch = new(axis.SelectedIndex == 1);
            strips.Orientation = stitch.Horizontal ? Orientation.Horizontal : Orientation.Vertical;
            strips.Children.Clear();
            stripCount = 0;
            countPill.Visibility = Visibility.Collapsed;
            await Read(manual: true);
        };
        automatic.Click += (_, _) => timer.IsEnabled = positioned && automatic.IsChecked == true;
        timer.Tick += async (_, _) => await Read(manual: false);
        SourceInitialized += (_, _) => ApplyChrome();
        ContentRendered += async (_, _) =>
        {
            positioned = CaptureWindowPlacement.Place(this, area);
            if (!positioned)
            {
                status.Text = "请缩小选区，为长截图控制窗口留出空间。";
                statusDot.SetResourceReference(Shape.FillProperty, "Danger");
                return;
            }
            await Read(manual: true);
            if (!closed) timer.IsEnabled = automatic.IsChecked == true;
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => { closed = true; timer.Stop(); };
    }

    internal void RefreshTheme() => ApplyChrome();

    private void ApplyChrome()
    {
        micaEnabled = Theme.ApplyWindowChrome(this, Native.BackdropKind.Mica);
        if (micaEnabled) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "Surface");
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
                double scale = Math.Min(1, 286.0 / (current.Horizontal ? image.PixelHeight : image.PixelWidth));
                strips.Children.Add(new Image { Source = image, Width = image.PixelWidth * scale,
                    Height = image.PixelHeight * scale, Stretch = Stretch.Fill });
                preview.UpdateLayout();
                if (current.Horizontal) preview.ScrollToRightEnd();
                else preview.ScrollToBottom();
                stripCount++;
                countBadge.Text = $"{stripCount} 屏";
                countPill.Visibility = Visibility.Visible;
                status.Text = $"{current.Width} × {current.Height} px";
                statusDot.SetResourceReference(Shape.FillProperty, "Positive");
            }
            else if (result == StitchResult.Limit)
            {
                status.Text = "已达到长截图像素上限。";
                statusDot.SetResourceReference(Shape.FillProperty, "Warning");
                timer.Stop();
            }
            else if (manual || result == StitchResult.NoMatch)
            {
                status.Text = result == StitchResult.Unchanged ? "画面未变动，滚动后再试。" : "没有稳定的重叠区域。请返回上次位置附近，再缓慢滚动。";
                statusDot.SetResourceReference(Shape.FillProperty, result == StitchResult.Unchanged ? "Muted" : "Warning");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            if (!closed)
            {
                status.Text = ex.Message;
                statusDot.SetResourceReference(Shape.FillProperty, "Danger");
            }
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
