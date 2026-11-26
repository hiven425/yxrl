using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Documents;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal sealed class ClockOverlay : Window
{
    private readonly TextBlock top = Line(), bottom = Line();
    private readonly StackPanel stack = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly nint handle;
    private PixelRect? placed, clip;
    private Size lastTextArea;
    private ClockText? lastText;
    public ClockOverlay()
    {
        Title = "YXRL 任务栏时钟";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        AllowsTransparency = true; ShowActivated = false; Focusable = false;
        Width = 60; Height = 32;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        stack.Children.Add(top); stack.Children.Add(bottom);
        Content = new Viewbox {
            IsHitTestVisible = false, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            Margin = new Thickness(1.5,1,1.5,1), Child = stack
        };
        handle = new WindowInteropHelper(this).EnsureHandle();
        // A layered transparent window passes hit-testing to the original Explorer HWND.
        const int exStyle = -20, transparent = 0x20, tool = 0x80, noActivate = 0x08000000;
        Native.SetStyle(handle,exStyle,Native.GetStyle(handle,exStyle) | transparent | tool | noActivate);
        AutomationProperties.SetAutomationId(this,"YxrlClock");
    }
    private static TextBlock Line()
    {
        var line = new TextBlock {
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None
        };
        Typography.SetNumeralAlignment(line,FontNumeralAlignment.Tabular);
        return line;
    }
    public bool Place(nint taskbar, ClockPlacement placement)
    {
        var bounds = placement.Bounds;
        if (clip != placement.Clip) {
            if (!Native.ClipWindow(handle,placement.Clip)) return false;
            clip = placement.Clip;
        }
        var above = Native.WindowAbove(taskbar);
        bool moved = placed != bounds;
        if (moved || above != handle) {
            uint flags = Native.NoActivate | Native.NoOwnerZOrder;
            if (!moved) flags |= Native.NoMove | Native.NoSize;
            if (above == handle) flags |= Native.NoZOrder;
            // Sit immediately over Explorer. Other windows that cover the taskbar
            // must cover its clock as well, including system menus and full-screen apps.
            var after = above == 0 && Native.IsTopmost(taskbar) ? Native.Topmost : above;
            if (!Native.SetWindowPos(handle,after,bounds.Left,bounds.Top,bounds.Width,bounds.Height,flags)) return false;
            placed = bounds;
        }
        if (!IsVisible) Show();
        return true;
    }
    public void Colorize(Color color)
    {
        Background = new SolidColorBrush(color);
        var ink = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 150 ? Brushes.Black : Brushes.White;
        top.Foreground = bottom.Foreground = ink;
    }
    public void Draw(ClockAnchor anchor, ClockText value)
    {
        var scale = anchor.Dpi / 96.0;
        var area = new Size(Math.Max(1,anchor.Bounds.Width / scale - 3),Math.Max(1,anchor.Bounds.Height / scale - 2));
        if (lastTextArea == area && lastText == value) return;
        lastTextArea = area;
        lastText = value;
        string upper = value.Upper.Length == 0 && value.Lower.Length == 0 ? value.Time : value.Upper;
        string lower = value.Lower;
        AutomationProperties.SetName(top,upper);
        AutomationProperties.SetName(bottom,lower);
        top.Visibility = upper.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        bottom.Visibility = lower.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        stack.Width = area.Width;
        var typeface = new Typeface(FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);

        // Measure the actual wrapped text and keep the largest size that fits.
        // The Viewbox also fits unusually long strings beyond the minimum font size.
        bool Fits(double font)
        {
            double Width(string value) => new FormattedText(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
                typeface,font,Brushes.Black,scale).Width;
            TextElement.SetFontSize(stack,font);
            top.Text = BreakAtSpaces(upper,area.Width,Width);
            bottom.Text = BreakAtSpaces(lower,area.Width,Width);
            stack.Measure(new Size(area.Width,double.PositiveInfinity));
            return stack.DesiredSize.Height <= area.Height;
        }
        var max = Math.Clamp(area.Height / 2.65,1,12.25);
        if (Fits(max)) return;
        double smallest = 1, largest = max;
        while (largest - smallest > 0.2) {
            var candidate = (smallest + largest) / 2;
            if (Fits(candidate)) smallest = candidate;
            else largest = candidate;
        }
        Fits(smallest);
    }
    private static string BreakAtSpaces(string text, double width, Func<string,double> measure)
    {
        // Prefer the actual spaces between format fields. WPF can still wrap
        // inside a long field if it cannot fit on a row by itself.
        StringBuilder? result = null;
        int start = 0, space = -1;
        for (int index = 0; index <= text.Length; index++) {
            char current = index == text.Length ? '\n' : text[index];
            if (current is not (' ' or '\n' or '\r')) continue;
            if (space >= start && measure(text[start..index]) > width) {
                result ??= new StringBuilder(text);
                result[space] = '\n';
                start = space + 1;
            }
            if (current == ' ') space = index;
            else { start = index + 1; space = -1; }
        }
        return result?.ToString() ?? text;
    }
    public static Color SampleBackground(nint taskbar, PixelRect bounds, PixelRect visible, PixelRect bar)
    {
        // Sample outside our own surface so recoloring never requires briefly hiding it.
        int x = bounds.Left > bar.Left ? bounds.Left-1 : bounds.Right;
        var point = new PixelPoint(x,visible.Top + visible.Height/5);
        if (!bar.Contains(point) || !Native.OnTaskbar(taskbar,point)) return Theme.SystemBar;
        var dc = Native.GetDC(0);
        if (dc == 0) return Theme.SystemBar;
        try {
            var rgb = Native.GetPixel(dc,point.X,point.Y);
            if (rgb == uint.MaxValue) return Theme.SystemBar;
            return Color.FromRgb((byte)(rgb & 255),(byte)((rgb >> 8)&255),(byte)((rgb >> 16)&255));
        } finally { Native.ReleaseDC(0,dc); }
    }
}
