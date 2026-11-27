using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed class TrimRangeBar : FrameworkElement
{
    private const double PadX = 8;
    private double total = 1, start, end = 1, playhead;
    private int dragMode; // 0 = none, 1 = start, 2 = end
    private int hoverMode;

    internal event Action<double, double, bool>? RangeChanged;

    public TrimRangeBar()
    {
        Height = 22;
        MinWidth = 120;
        Focusable = false;
        ToolTip = "拖动左右手柄剪裁开始与结束位置";
        AutomationProperties.SetName(this, "可视化剪辑范围");
    }

    public void SetValues(double totalSeconds, double startSeconds, double endSeconds, double playheadSeconds)
    {
        total = Math.Max(0.05, totalSeconds);
        start = Math.Clamp(startSeconds, 0, total);
        end = Math.Clamp(endSeconds, start, total);
        playhead = Math.Clamp(playheadSeconds, 0, total);
        InvalidateVisual();
    }

    private double ValueToX(double value)
    {
        double trackW = Math.Max(1, ActualWidth - PadX * 2);
        return PadX + Math.Clamp(value / total, 0, 1) * trackW;
    }

    private double XToValue(double x)
    {
        double trackW = Math.Max(1, ActualWidth - PadX * 2);
        return Math.Clamp((x - PadX) / trackW, 0, 1) * total;
    }

    private int HitTestHandle(Point p)
    {
        double xs = ValueToX(start), xe = ValueToX(end);
        double ds = Math.Abs(p.X - xs), de = Math.Abs(p.X - xe);
        const double threshold = 11;
        if (ds <= threshold && de <= threshold)
            return p.X <= (xs + xe) / 2 ? 1 : 2;
        if (ds <= threshold) return 1;
        if (de <= threshold) return 2;
        return 0;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        if (dragMode != 0 && IsMouseCaptured)
        {
            double val = XToValue(pos.X);
            double minGap = Math.Min(0.1, total * 0.1);
            if (dragMode == 1)
            {
                start = Math.Clamp(val, 0, Math.Max(0, end - minGap));
                RangeChanged?.Invoke(start, end, true);
            }
            else
            {
                end = Math.Clamp(val, Math.Min(total, start + minGap), total);
                RangeChanged?.Invoke(start, end, false);
            }
            InvalidateVisual();
            return;
        }
        int nextHover = HitTestHandle(pos);
        if (nextHover != hoverMode)
        {
            hoverMode = nextHover;
            Cursor = hoverMode != 0 ? Cursors.SizeWE : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (dragMode == 0 && hoverMode != 0)
        {
            hoverMode = 0;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        int hit = HitTestHandle(e.GetPosition(this));
        if (hit == 0)
            return;
        e.Handled = true;
        dragMode = hit;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragMode == 0)
            return;
        e.Handled = true;
        dragMode = 0;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= PadX * 2 || ActualHeight <= 4)
            return;
        double trackY = ActualHeight / 2;
        double trackW = ActualWidth - PadX * 2;
        double xs = ValueToX(start), xe = ValueToX(end), xp = ValueToX(playhead);

        var trackBrush = Application.Current?.TryFindResource("Line") as Brush ?? new SolidColorBrush(Color.FromRgb(210, 220, 232));
        var accentBrush = Application.Current?.TryFindResource("Accent") as Brush ?? new SolidColorBrush(Color.FromRgb(11, 117, 209));
        var paperBrush = Application.Current?.TryFindResource("Paper") as Brush ?? Brushes.White;
        var inkBrush = Application.Current?.TryFindResource("Ink") as Brush ?? Brushes.Black;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRoundedRectangle(trackBrush, null, new Rect(PadX, trackY - 3, trackW, 6), 3, 3);
        if (xe > xs)
            dc.DrawRoundedRectangle(accentBrush, null, new Rect(xs, trackY - 3, xe - xs, 6), 3, 3);

        // Playhead indicator
        dc.DrawRoundedRectangle(inkBrush, null, new Rect(xp - 1, trackY - 6, 2, 12), 1, 1);

        DrawHandle(dc, xs, trackY, dragMode == 1 || hoverMode == 1, accentBrush, paperBrush);
        DrawHandle(dc, xe, trackY, dragMode == 2 || hoverMode == 2, accentBrush, paperBrush);
    }

    private static void DrawHandle(DrawingContext dc, double cx, double cy, bool active, Brush accentBrush, Brush paperBrush)
    {
        double w = active ? 11 : 10, h = active ? 18 : 16;
        var pen = new Pen(accentBrush, active ? 1.8 : 1.3);
        pen.Freeze();
        dc.DrawRoundedRectangle(paperBrush, pen, new Rect(cx - w / 2, cy - h / 2, w, h), 4, 4);
        dc.DrawLine(pen, new Point(cx, cy - 4), new Point(cx, cy + 4));
    }
}

internal sealed class CropOverlay : FrameworkElement
{
    private int clipW = 1, clipH = 1;
    private PixelRect crop = new(0, 0, 1, 1);
    private string? activeEdge, hoverEdge;
    private PixelRect dragOrigin;
    private Point dragAnchor;

    internal event Action<PixelRect>? CropChanged;

    public CropOverlay()
    {
        Cursor = Cursors.Arrow;
        AutomationProperties.SetName(this, "视频画面裁剪蒙版");
    }

    public void SetClip(int clipWidth, int clipHeight, PixelRect cropBounds)
    {
        clipW = Math.Max(1, clipWidth);
        clipH = Math.Max(1, clipHeight);
        crop = cropBounds;
        InvalidateVisual();
    }

    private (double Scale, double X0, double Y0, Rect VideoRect, Rect CropRect) Metrics()
    {
        double scale = Math.Min(ActualWidth / clipW, ActualHeight / clipH);
        if (scale <= 0 || !double.IsFinite(scale))
            return (0, 0, 0, Rect.Empty, Rect.Empty);
        double vw = clipW * scale, vh = clipH * scale;
        double x0 = (ActualWidth - vw) / 2, y0 = (ActualHeight - vh) / 2;
        var vidRect = new Rect(x0, y0, vw, vh);
        var selRect = new Rect(x0 + crop.Left * scale, y0 + crop.Top * scale, crop.Width * scale, crop.Height * scale);
        return (scale, x0, y0, vidRect, selRect);
    }

    private string? HitTestEdge(Point p, Rect selRect)
    {
        if (selRect.IsEmpty)
            return null;
        const double grab = 10;
        if (p.X < selRect.Left - grab || p.X > selRect.Right + grab || p.Y < selRect.Top - grab || p.Y > selRect.Bottom + grab)
            return null;
        bool nearL = Math.Abs(p.X - selRect.Left) <= grab;
        bool nearR = Math.Abs(p.X - selRect.Right) <= grab;
        bool nearT = Math.Abs(p.Y - selRect.Top) <= grab;
        bool nearB = Math.Abs(p.Y - selRect.Bottom) <= grab;
        if (nearT && nearL) return "nw";
        if (nearT && nearR) return "ne";
        if (nearB && nearL) return "sw";
        if (nearB && nearR) return "se";
        if (nearL) return "w";
        if (nearR) return "e";
        if (nearT) return "n";
        if (nearB) return "s";
        return null;
    }

    private static Cursor CursorFor(string? edge) => edge switch
    {
        "n" or "s" => Cursors.SizeNS,
        "w" or "e" => Cursors.SizeWE,
        "nw" or "se" => Cursors.SizeNWSE,
        "ne" or "sw" => Cursors.SizeNESW,
        _ => Cursors.Arrow
    };

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        var m = Metrics();
        if (m.Scale <= 0)
            return;

        if (activeEdge is not null && IsMouseCaptured)
        {
            int dx = (int)Math.Round((p.X - dragAnchor.X) / m.Scale);
            int dy = (int)Math.Round((p.Y - dragAnchor.Y) / m.Scale);
            int minEdge = Math.Min(Mp4Writer.MinimumEdge, Math.Min(clipW, clipH));
            int l = dragOrigin.Left, t = dragOrigin.Top, r = dragOrigin.Right, b = dragOrigin.Bottom;

            if (activeEdge.Contains('w'))
                l = Math.Clamp(l + dx, 0, r - minEdge);
            if (activeEdge.Contains('e'))
                r = Math.Clamp(r + dx, l + minEdge, clipW);
            if (activeEdge.Contains('n'))
                t = Math.Clamp(t + dy, 0, b - minEdge);
            if (activeEdge.Contains('s'))
                b = Math.Clamp(b + dy, t + minEdge, clipH);

            // Keep MP4 dimensions even when dragging edges.
            if (((r - l) & 1) != 0)
            {
                if (activeEdge.Contains('w') && l + 1 <= r - minEdge) l++;
                else if (r - 1 >= l + minEdge) r--;
            }
            if (((b - t) & 1) != 0)
            {
                if (activeEdge.Contains('n') && t + 1 <= b - minEdge) t++;
                else if (b - 1 >= t + minEdge) b--;
            }

            var next = new PixelRect(l, t, r, b);
            if (next != crop)
            {
                crop = next;
                InvalidateVisual();
                CropChanged?.Invoke(next);
            }
            return;
        }

        string? hit = HitTestEdge(p, m.CropRect);
        if (hit != hoverEdge)
        {
            hoverEdge = hit;
            Cursor = CursorFor(hit);
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (activeEdge is null && hoverEdge is not null)
        {
            hoverEdge = null;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var m = Metrics();
        if (m.Scale <= 0)
            return;
        var p = e.GetPosition(this);
        var hit = HitTestEdge(p, m.CropRect);
        if (hit is null)
            return;
        e.Handled = true;
        activeEdge = hit;
        dragOrigin = crop;
        dragAnchor = p;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (activeEdge is null)
            return;
        e.Handled = true;
        activeEdge = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var m = Metrics();
        if (m.Scale <= 0)
            return;
        var v = m.VideoRect;
        var c = m.CropRect;

        // Provide an invisible hit border near video edges while leaving center clicks unobstructed.
        bool isCropped = crop.Left > 0 || crop.Top > 0 || crop.Right < clipW || crop.Bottom < clipH;
        if (isCropped)
        {
            var dim = new SolidColorBrush(Color.FromArgb(148, 8, 13, 20));
            dim.Freeze();
            if (c.Top > v.Top) dc.DrawRectangle(dim, null, new Rect(v.Left, v.Top, v.Width, c.Top - v.Top));
            if (c.Bottom < v.Bottom) dc.DrawRectangle(dim, null, new Rect(v.Left, c.Bottom, v.Width, v.Bottom - c.Bottom));
            if (c.Left > v.Left) dc.DrawRectangle(dim, null, new Rect(v.Left, c.Top, c.Left - v.Left, c.Height));
            if (c.Right < v.Right) dc.DrawRectangle(dim, null, new Rect(c.Right, c.Top, v.Right - c.Right, c.Height));
        }

        // Thin transparent perimeter stroke makes edge handles clickable even before cropping.
        var hitEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), 14);
        hitEdgePen.Freeze();
        dc.DrawRectangle(null, hitEdgePen, c);

        var lineBrush = new SolidColorBrush(Color.FromArgb((byte)(isCropped || hoverEdge is not null || activeEdge is not null ? 235 : 115), 45, 156, 255));
        lineBrush.Freeze();
        var borderPen = new Pen(lineBrush, isCropped ? 1.5 : 1.0);
        borderPen.Freeze();
        dc.DrawRectangle(null, borderPen, c);

        // Edge grabbers on all 4 sides
        var gripBrush = Brushes.White;
        var gripPen = new Pen(lineBrush, 1.2);
        gripPen.Freeze();
        dc.DrawRoundedRectangle(gripBrush, gripPen, new Rect(c.Left + c.Width / 2 - 11, c.Top - 2, 22, 4), 2, 2);
        dc.DrawRoundedRectangle(gripBrush, gripPen, new Rect(c.Left + c.Width / 2 - 11, c.Bottom - 2, 22, 4), 2, 2);
        dc.DrawRoundedRectangle(gripBrush, gripPen, new Rect(c.Left - 2, c.Top + c.Height / 2 - 11, 4, 22), 2, 2);
        dc.DrawRoundedRectangle(gripBrush, gripPen, new Rect(c.Right - 2, c.Top + c.Height / 2 - 11, 4, 22), 2, 2);
    }
}

internal sealed class ProgressPreview : FrameworkElement
{
    private int clipW = 1, clipH = 1;
    private PixelRect crop = new(0, 0, 1, 1);
    private long elapsedTicks, totalTicks;
    private RecordingProgressStyle style = RecordingProgressStyle.None;

    public ProgressPreview()
    {
        IsHitTestVisible = false;
    }

    public void Set(int clipWidth, int clipHeight, PixelRect cropRect, long elapsed, long total, RecordingProgressStyle progressStyle)
    {
        clipW = Math.Max(1, clipWidth);
        clipH = Math.Max(1, clipHeight);
        crop = cropRect;
        elapsedTicks = elapsed;
        totalTicks = total;
        style = progressStyle;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (style == RecordingProgressStyle.None || crop.Width < 16 || crop.Height < 16)
            return;
        double scale = Math.Min(ActualWidth / clipW, ActualHeight / clipH);
        if (scale <= 0 || !double.IsFinite(scale))
            return;
        double x0 = (ActualWidth - clipW * scale) / 2 + crop.Left * scale;
        double y0 = (ActualHeight - clipH * scale) / 2 + crop.Top * scale;
        double cw = crop.Width * scale, ch = crop.Height * scale;

        if (style == RecordingProgressStyle.Bar)
        {
            double barH = Math.Clamp(Math.Round(crop.Height / 72.0), 3, 8) * scale;
            double y = y0 + ch - barH;
            var track = new SolidColorBrush(Color.FromArgb(175, 16, 26, 38));
            track.Freeze();
            dc.DrawRectangle(track, null, new Rect(x0, y, cw, barH));
            double fillW = cw * RecordingProgressPainter.ProgressFraction(elapsedTicks, totalTicks);
            if (fillW > 0.5)
            {
                var fill = new SolidColorBrush(Color.FromArgb(245, 45, 156, 255));
                fill.Freeze();
                dc.DrawRectangle(fill, null, new Rect(x0, y, fillW, barH));
            }
        }
        else
        {
            long shown = style == RecordingProgressStyle.Elapsed
                ? Math.Clamp(elapsedTicks, 0, Math.Max(0, totalTicks))
                : Math.Max(0, totalTicks - Math.Max(0, elapsedTicks));
            string label = RecordingProgressPainter.FormatClock(shown);
            double unit = Math.Clamp(crop.Height / 360.0, 0.55, 2.4) * scale;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var font = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, font, 14 * unit, Brushes.White, dpi);
            double padX = 7 * unit, margin = 8 * unit, pillH = 22 * unit, pillW = text.Width + padX * 2;
            double x = Math.Max(x0 + 4 * unit, x0 + cw - pillW - margin);
            double y = Math.Max(y0 + 4 * unit, y0 + margin);
            var bg = new SolidColorBrush(Color.FromArgb(214, 16, 28, 44));
            bg.Freeze();
            dc.DrawRoundedRectangle(bg, null, new Rect(x, y, pillW, pillH), 5 * unit, 5 * unit);
            dc.DrawText(text, new Point(x + padX, y + (pillH - text.Height) / 2));
        }
    }
}
