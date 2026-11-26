using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal sealed class ClockOverlay : Window
{
    private readonly TextBlock top = Line(), bottom = Line();
    private readonly StackPanel stack = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly nint handle;
    private ClockAnchor? placed;
    public ClockOverlay()
    {
        Title = "YXRL 任务栏时钟";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        AllowsTransparency = true; ShowActivated = false; Focusable = false; Topmost = true;
        Width = 60; Height = 32; Opacity = 0;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        stack.Children.Add(top); stack.Children.Add(bottom);
        Content = new Grid { IsHitTestVisible = false, Children = { stack } };
        handle = new WindowInteropHelper(this).EnsureHandle();
        // A layered transparent window passes hit-testing to the original Explorer HWND.
        const int exStyle = -20, transparent = 0x20, tool = 0x80, noActivate = 0x08000000;
        Native.SetStyle(handle,exStyle,Native.GetStyle(handle,exStyle) | transparent | tool | noActivate);
        AutomationProperties.SetAutomationId(this,"YxrlClock");
    }
    private static TextBlock Line() => new() { FontSize = 12, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, SnapsToDevicePixels = true };
    public void Place(ClockAnchor anchor)
    {
        bool moved = placed != anchor || !IsVisible;
        placed = anchor;
        if (!IsVisible) Show();
        if (moved) {
            var b = anchor.Bounds;
            Native.SetWindowPos(handle,Native.Topmost,b.Left,b.Top,b.Width,b.Height,Native.NoActivate | Native.Show);
            Opacity = 1;
        }
    }
    public void Colorize(Color color)
    {
        Background = new SolidColorBrush(color);
        var ink = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 150 ? Brushes.Black : Brushes.White;
        top.Foreground = bottom.Foreground = ink;
    }
    public void Draw(ClockText value, DateTime now)
    {
        if (placed is null || !IsVisible) return;
        var scale = placed.Dpi / 96.0;
        double width = Math.Max(1,placed.Bounds.Width / scale - 4);
        var height = placed.Bounds.Height / scale;
        var fontSize = Math.Min(12.25, Math.Max(8, (height - 2) / 2.65));
        var type = new Typeface(FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
        double Measure(string s) => new FormattedText(s,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
            type,fontSize,Brushes.Black,scale).WidthIncludingTrailingWhitespace;
        var (upper,lower) = ClockFormatter.Fit(value,width,Measure,now.Ticks / TimeSpan.FromSeconds(4).Ticks);
        top.FontSize = Measure(upper) > width ? Math.Max(6, fontSize * width / Measure(upper)) : fontSize;
        bottom.FontSize = fontSize;
        if (top.Text != upper) top.Text = upper;
        if (bottom.Text != lower) bottom.Text = lower;
        top.Visibility = upper.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        bottom.Visibility = lower.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    public static Color SampleBackground(ClockAnchor a)
    {
        var dc = Native.GetDC(0);
        if (dc == 0) return Theme.SystemBar;
        try {
            var p = a.Bounds; var rgb = Native.GetPixel(dc,p.Left + 1,p.Top + Math.Max(1,p.Height/5));
            if (rgb == uint.MaxValue) return Theme.SystemBar;
            return Color.FromRgb((byte)(rgb & 255),(byte)((rgb >> 8)&255),(byte)((rgb >> 16)&255));
        } finally { Native.ReleaseDC(0,dc); }
    }
}
