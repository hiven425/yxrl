using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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
using Microsoft.Win32;
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
        autoScrollBtn = new() { Content = "自动滚动" },
        resetBtn = new() { Content = "清空重截" },
        finish = new() { Content = "完成长截图", IsEnabled = false },
        copyBtn = new() { Content = "复制", IsEnabled = false },
        saveBtn = new() { Content = "保存", IsEnabled = false };
    private readonly StackPanel strips = new();
    private readonly ScrollViewer preview = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(420) },
        autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private readonly RecordingRegionFrame regionFrame;
    internal RecordingRegionFrame RegionFrame => regionFrame;
    private ScrollStitch stitch = new(horizontal: false);
    private bool positioned, busy, closed, micaEnabled, autoScrolling;
    private int stripCount, unchangedAutoTicks;

    public ScrollWindow(PixelRect area, Action<BitmapSource> showPin)
    {
        this.area = area;
        this.showPin = showPin;
        regionFrame = new RecordingRegionFrame(area);
        Title = "YXRL 长截图";
        if (Theme.AppIcon is { } appIcon) Icon = appIcon;
        AutomationProperties.SetAutomationId(this, "YxrlScrollCapture");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Width = 376;
        Height = 424;
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
        var captureActions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
        next.SetResourceReference(StyleProperty, "SecondaryButton");
        autoScrollBtn.SetResourceReference(StyleProperty, "SecondaryButton");
        resetBtn.SetResourceReference(StyleProperty, "SecondaryButton");
        autoScrollBtn.ToolTip = "自动向选区内发送滚轮事件（快捷键 A）";
        AutomationProperties.SetName(autoScrollBtn, "自动滚动");
        AutomationProperties.SetName(resetBtn, "清空重截");
        for (int i = 0; i < 3; i++)
        {
            var btn = i == 0 ? next : i == 1 ? autoScrollBtn : resetBtn;
            btn.MinHeight = 29;
            btn.Padding = new Thickness(6, 4, 6, 4);
            btn.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0);
            captureActions.Children.Add(btn);
        }
        bottomStack.Children.Add(captureActions);

        var exportActions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
        finish.SetResourceReference(StyleProperty, "PrimaryButton");
        copyBtn.SetResourceReference(StyleProperty, "SecondaryButton");
        saveBtn.SetResourceReference(StyleProperty, "SecondaryButton");
        AutomationProperties.SetName(copyBtn, "复制长截图");
        AutomationProperties.SetName(saveBtn, "保存长截图");
        for (int i = 0; i < 3; i++)
        {
            var btn = i == 0 ? finish : i == 1 ? copyBtn : saveBtn;
            btn.MinHeight = 30;
            btn.Padding = new Thickness(6, 4, 6, 4);
            btn.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0);
            exportActions.Children.Add(btn);
        }
        bottomStack.Children.Add(exportActions);

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
        autoScrollBtn.Click += (_, _) => ToggleAutoScroll();
        resetBtn.Click += async (_, _) => await ResetCapture();
        finish.Click += async (_, _) => await Complete();
        copyBtn.Click += async (_, _) => await CopyResult();
        saveBtn.Click += async (_, _) => await SaveResult();
        axis.SelectionChanged += async (_, _) => await ResetCapture();
        automatic.Click += (_, _) => timer.IsEnabled = positioned && automatic.IsChecked == true;
        timer.Tick += async (_, _) => await Read(manual: false);
        autoScrollTimer.Tick += async (_, _) => await StepAutoScroll();
        SourceInitialized += (_, _) =>
        {
            ApplyChrome();
            regionFrame.UpdateState(RecordingFrameState.Scrolling, badgeLabel: "长截图区域");
            regionFrame.Show();
        };
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
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.None && !axis.IsDropDownOpen)
            {
                e.Handled = true;
                ToggleAutoScroll();
            }
        };
        Closed += (_, _) =>
        {
            closed = true;
            timer.Stop();
            autoScrollTimer.Stop();
            regionFrame.CloseSafely();
        };
    }

    internal void RefreshTheme() => ApplyChrome();

    private void ApplyChrome()
    {
        micaEnabled = Theme.ApplyWindowChrome(this, Native.BackdropKind.Mica);
        if (micaEnabled) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "Surface");
    }

    private void ToggleAutoScroll()
    {
        if (closed || !positioned)
            return;
        if (autoScrolling)
        {
            StopAutoScroll("已停止自动滚动");
            return;
        }
        autoScrolling = true;
        unchangedAutoTicks = 0;
        autoScrollBtn.Content = "停止滚动";
        autoScrollBtn.SetResourceReference(Control.BackgroundProperty, "Hot");
        autoScrollBtn.SetResourceReference(Control.BorderBrushProperty, "Accent");
        autoScrollBtn.SetResourceReference(Control.ForegroundProperty, "Accent");
        regionFrame.UpdateState(RecordingFrameState.Scrolling, badgeLabel: "自动滚动中 · 按 A 停止");
        autoScrollTimer.Start();
    }

    private void StopAutoScroll(string? reason = null)
    {
        autoScrolling = false;
        unchangedAutoTicks = 0;
        autoScrollTimer.Stop();
        autoScrollBtn.Content = "自动滚动";
        autoScrollBtn.SetResourceReference(Control.BackgroundProperty, "Paper");
        autoScrollBtn.SetResourceReference(Control.BorderBrushProperty, "PanelBorder");
        autoScrollBtn.SetResourceReference(Control.ForegroundProperty, "Ink");
        regionFrame.UpdateState(RecordingFrameState.Scrolling, badgeLabel: "长截图区域");
        if (!string.IsNullOrEmpty(reason) && !closed)
        {
            status.Text = reason;
            statusDot.SetResourceReference(Shape.FillProperty, "Muted");
        }
    }

    private async Task StepAutoScroll()
    {
        if (!autoScrolling || busy || closed || !positioned)
            return;
        if (!Native.GetCursorPos(out var pt) || !area.Contains(pt))
        {
            status.Text = "请将鼠标移入长截图选区内以自动滚动";
            statusDot.SetResourceReference(Shape.FillProperty, "Warning");
            return;
        }
        Native.SendMouseWheel(stitch.Horizontal ? 120 : -120, stitch.Horizontal);
        var result = await Read(manual: false);
        if (!autoScrolling || closed)
            return;
        if (result == StitchResult.Added)
            unchangedAutoTicks = 0;
        else if (result is StitchResult.Unchanged or StitchResult.NoMatch && ++unchangedAutoTicks >= 6)
            StopAutoScroll("已滚动到底部或画面无变化，已自动停止");
    }

    internal async Task ResetCapture()
    {
        if (closed)
            return;
        StopAutoScroll();
        timer.Stop();
        stitch = new(axis.SelectedIndex == 1);
        strips.Orientation = stitch.Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        strips.Children.Clear();
        stripCount = 0;
        countPill.Visibility = Visibility.Collapsed;
        finish.IsEnabled = copyBtn.IsEnabled = saveBtn.IsEnabled = false;
        await Read(manual: true);
        if (!closed && positioned)
            timer.IsEnabled = automatic.IsChecked == true;
    }

    private async Task<StitchResult?> Read(bool manual)
    {
        if (busy || closed || !positioned)
            return null;
        busy = true;
        next.IsEnabled = finish.IsEnabled = copyBtn.IsEnabled = saveBtn.IsEnabled = axis.IsEnabled = resetBtn.IsEnabled = false;
        var current = stitch;
        StitchResult? outcome = null;
        try
        {
            var result = await Task.Run(() =>
            {
                if (DesktopFrame.SessionLocked() || !DesktopFrame.HasActiveDisplay())
                    throw new InvalidOperationException("Windows 桌面暂不可捕获。");
                using var pixels = new ScreenPixels(area);
                return current.Append(pixels.Image());
            });
            outcome = result;
            if (closed)
                return outcome;
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
                StopAutoScroll();
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
            StopAutoScroll();
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
            next.IsEnabled = axis.IsEnabled = resetBtn.IsEnabled = true;
            bool hasAny = current.LastStrip is not null;
            finish.IsEnabled = copyBtn.IsEnabled = saveBtn.IsEnabled = hasAny;
        }
        return outcome;
    }

    private async Task Complete()
    {
        if (busy || stitch.LastStrip is null)
            return;
        StopAutoScroll();
        timer.Stop();
        busy = true;
        next.IsEnabled = finish.IsEnabled = copyBtn.IsEnabled = saveBtn.IsEnabled = axis.IsEnabled = resetBtn.IsEnabled = false;
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
                statusDot.SetResourceReference(Shape.FillProperty, "Danger");
                next.IsEnabled = finish.IsEnabled = copyBtn.IsEnabled = saveBtn.IsEnabled = axis.IsEnabled = resetBtn.IsEnabled = true;
                busy = false;
            }
        }
    }

    private async Task CopyResult()
    {
        if (busy || stitch.LastStrip is null)
            return;
        StopAutoScroll();
        timer.Stop();
        busy = true;
        next.IsEnabled = finish.IsEnabled = copyBtn.IsEnabled = saveBtn.IsEnabled = axis.IsEnabled = resetBtn.IsEnabled = false;
        try
        {
            var result = await Task.Run(stitch.Finish);
            if (!closed)
            {
                Clipboard.SetImage(result);
                status.Text = $"已复制 {result.PixelWidth} × {result.PixelHeight} 长截图";
                statusDot.SetResourceReference(Shape.FillProperty, "Positive");
            }
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException)
        {
            if (!closed)
            {
                status.Text = "复制失败。 " + ex.Message;
                statusDot.SetResourceReference(Shape.FillProperty, "Danger");
            }
        }
        finally
        {
            busy = false;
            next.IsEnabled = axis.IsEnabled = resetBtn.IsEnabled = true;
            bool hasAny = stitch.LastStrip is not null;
            finish.IsEnabled = copyBtn.IsEnabled = saveBtn.IsEnabled = hasAny;
        }
    }

    private async Task SaveResult()
    {
        if (busy || stitch.LastStrip is null)
            return;
        StopAutoScroll();
        timer.Stop();
        busy = true;
        next.IsEnabled = finish.IsEnabled = copyBtn.IsEnabled = saveBtn.IsEnabled = axis.IsEnabled = resetBtn.IsEnabled = false;
        try
        {
            var result = await Task.Run(stitch.Finish);
            if (closed)
                return;
            var dialog = new SaveFileDialog
            {
                Filter = "PNG 图片 (*.png)|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = $"YXRL-长截图-{DateTime.Now:yyyyMMdd-HHmmss}.png"
            };
            if (dialog.ShowDialog(this) == true)
            {
                CaptureTools.SavePng(new ShotDocument(result), dialog.FileName);
                status.Text = "已保存 " + System.IO.Path.GetFileName(dialog.FileName);
                statusDot.SetResourceReference(Shape.FillProperty, "Positive");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or InvalidOperationException or ArgumentException)
        {
            if (!closed)
            {
                status.Text = "保存失败。 " + ex.Message;
                statusDot.SetResourceReference(Shape.FillProperty, "Danger");
            }
        }
        finally
        {
            busy = false;
            next.IsEnabled = axis.IsEnabled = resetBtn.IsEnabled = true;
            bool hasAny = stitch.LastStrip is not null;
            finish.IsEnabled = copyBtn.IsEnabled = saveBtn.IsEnabled = hasAny;
        }
    }
}
