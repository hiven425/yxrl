using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal enum MarkKind
{
    Rectangle,
    Arrow,
    Pen,
    Text,
    Mosaic,
    Ellipse,
    Line,
    Highlight,
    Number
}

internal sealed record MarkStyle(Color Color, double Size, bool Bold = false, bool Background = false, bool Square = false,
    bool Italic = false, bool Outline = false, string Font = "Segoe UI, Microsoft YaHei UI")
{
    public static readonly Color Red = Color.FromRgb(212, 64, 47);
    public static MarkStyle For(MarkKind kind) => new(Red, kind switch
    {
        MarkKind.Text => 20,
        MarkKind.Number => 28,
        MarkKind.Highlight => 18,
        _ => 3
    });
}

internal sealed record ShotMark(MarkKind Kind, Point[] Points, string Text = "", int Number = 0, MarkStyle? Style = null,
    double? TextWidth = null)
{
    public Rect Bounds => new(Points[0], Points[^1]);
    public MarkStyle Appearance => Style ?? MarkStyle.For(Kind);

    public ShotMark Offset(double x, double y) => this with
    {
        Points = Points.Select(p => new Point(p.X + x, p.Y + y)).ToArray()
    };
}

internal sealed class ShotDocument
{
    private sealed record Edit(int Index, ShotMark? Before, ShotMark? After, ShotMark[]? Cleared = null);

    public BitmapSource Image { get; private set; }
    public int Width => Image.PixelWidth;
    public int Height => Image.PixelHeight;
    public PixelRect Full => new(0, 0, Width, Height);

    public void ReplaceImage(BitmapSource image)
    {
        if (!image.IsFrozen && image.CanFreeze)
            image.Freeze();
        Image = image;
    }

    private readonly List<ShotMark> marks = [];
    private readonly Stack<Edit> undo = new(), redo = new();
    public bool HasMarks => marks.Count > 0;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public int NextNumber => Math.Clamp(marks.Count == 0 ? 1 : marks.Max(mark => mark.Number) + 1, 1, 9999);

    public ShotDocument(BitmapSource image)
    {
        Image = image;
        if (!image.IsFrozen && image.CanFreeze)
            image.Freeze();
    }

    public void Add(ShotMark mark) => Apply(new(marks.Count, null, mark));

    public void ClearMarks()
    {
        if (marks.Count == 0)
            return;
        Apply(new(0, null, null, marks.ToArray()));
    }

    public ShotMark? EditableAt(MarkKind kind, Point point)
    {
        if (kind is MarkKind.Pen or MarkKind.Highlight)
            return null;
        return marks.LastOrDefault(mark => mark.Kind == kind && HitMark(mark, point));
    }

    private bool HitMark(ShotMark mark, Point point)
    {
        if (mark.Kind is MarkKind.Text or MarkKind.Number)
            return EditableBounds(mark).Contains(point);
        double tol = Math.Max(8, mark.Appearance.Size + 5);
        if (mark.Kind is MarkKind.Line or MarkKind.Arrow)
            return DistanceToSegment(point, mark.Points[0], mark.Points[^1]) <= tol;
        var bounds = mark.Bounds;
        var outer = Rect.Inflate(bounds, tol, tol);
        if (!outer.Contains(point))
            return false;
        if (mark.Kind == MarkKind.Mosaic)
            return true;
        if (bounds.Width > tol * 2 && bounds.Height > tol * 2)
            return !Rect.Inflate(bounds, -tol, -tol).Contains(point);
        return true;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-6)
            return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        return (p - new Point(a.X + t * dx, a.Y + t * dy)).Length;
    }

    public Rect EditableBounds(ShotMark mark)
    {
        var a = mark.Points[0];
        return mark.Kind switch
        {
            MarkKind.Number => new Rect(a.X - mark.Appearance.Size / 2, a.Y - mark.Appearance.Size / 2, mark.Appearance.Size, mark.Appearance.Size),
            MarkKind.Text => TextBounds(a, FormatText(mark, Brushes.Black)),
            MarkKind.Line or MarkKind.Arrow => Rect.Inflate(mark.Bounds, Math.Max(6, mark.Appearance.Size), Math.Max(6, mark.Appearance.Size)),
            MarkKind.Rectangle or MarkKind.Ellipse or MarkKind.Mosaic => Rect.Inflate(mark.Bounds, Math.Max(3, mark.Appearance.Size / 2), Math.Max(3, mark.Appearance.Size / 2)),
            _ => mark.Bounds
        };
    }

    public static (string Id, Point Point)[] MarkHandles(ShotMark mark) => mark.Kind switch
    {
        MarkKind.Line or MarkKind.Arrow => [("start", mark.Points[0]), ("end", mark.Points[^1])],
        MarkKind.Rectangle or MarkKind.Ellipse or MarkKind.Mosaic => [
            ("nw", mark.Bounds.TopLeft),
            ("ne", mark.Bounds.TopRight),
            ("sw", mark.Bounds.BottomLeft),
            ("se", mark.Bounds.BottomRight)
        ],
        _ => []
    };

    public static ShotMark ResizeMark(ShotMark mark, string handle, Point target)
    {
        if (mark.Kind is MarkKind.Line or MarkKind.Arrow)
        {
            return handle == "start"
                ? mark with { Points = [target, mark.Points[^1]] }
                : mark with { Points = [mark.Points[0], target] };
        }
        var r = mark.Bounds;
        double fixedX = handle.Contains('w') ? r.Right : r.Left;
        double fixedY = handle.Contains('n') ? r.Bottom : r.Top;
        return mark with { Points = [new(Math.Min(fixedX, target.X), Math.Min(fixedY, target.Y)), new(Math.Max(fixedX, target.X), Math.Max(fixedY, target.Y))] };
    }

    public void Replace(ShotMark before, ShotMark? after)
    {
        int index = marks.IndexOf(before);
        if (index >= 0 && after != before)
            Apply(new(index, before, after));
    }

    private void Apply(Edit change)
    {
        if (change.Cleared is not null)
            marks.Clear();
        else
            Update(change.Index, change.Before, change.After);
        undo.Push(change);
        redo.Clear();
    }

    private void Update(int index, ShotMark? oldValue, ShotMark? newValue)
    {
        if (newValue is null)
            marks.RemoveAt(index);
        else if (oldValue is null)
            marks.Insert(index, newValue);
        else
            marks[index] = newValue;
    }

    public void Undo()
    {
        if (undo.TryPop(out var change))
        {
            if (change.Cleared is { } saved)
            {
                marks.Clear();
                marks.AddRange(saved);
            }
            else
                Update(change.Index, change.After, change.Before);
            redo.Push(change);
        }
    }

    public void Redo()
    {
        if (redo.TryPop(out var change))
        {
            if (change.Cleared is not null)
                marks.Clear();
            else
                Update(change.Index, change.Before, change.After);
            undo.Push(change);
        }
    }

    public ShotDocument Crop(PixelRect rect)
    {
        rect = rect.Intersect(Full);
        if (!rect.HasArea)
            throw new ArgumentException("截图选区为空。");
        // A pin may outlive the selection. Keep only its selected pixels in memory.
        var pixels = rect == Full ? Image : new WriteableBitmap(
            new CroppedBitmap(Image, new Int32Rect(rect.Left, rect.Top, rect.Width, rect.Height)));
        var snapshot = new ShotDocument(pixels);
        foreach (var m in marks)
            snapshot.Add(m.Offset(-rect.Left, -rect.Top));
        return snapshot;
    }

    public void Draw(DrawingContext dc, ShotMark? pending = null, bool forOcr = false, ShotMark? replaces = null, bool drawImage = true)
    {
        if (drawImage)
            dc.DrawImage(Image, new Rect(0, 0, Width, Height));
        foreach (var mark in marks)
            Paint(dc, mark == replaces ? pending ?? mark : mark);
        if (pending is not null && replaces is null)
            Paint(dc, pending);
        if (forOcr)
            foreach (var mark in marks.Where(m => m.Kind == MarkKind.Mosaic))
                dc.DrawRectangle(Brushes.Black, null, mark.Bounds);
    }

    private void Paint(DrawingContext dc, ShotMark mark)
    {
        var a = mark.Points[0];
        var b = mark.Points[^1];
        var style = mark.Appearance;
        var brush = new SolidColorBrush(style.Color);
        var stroke = new Pen(brush, style.Size)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        switch (mark.Kind)
        {
            case MarkKind.Rectangle:
                dc.DrawRectangle(null, stroke, mark.Bounds);
                break;
            case MarkKind.Ellipse:
                dc.DrawGeometry(null, stroke, new EllipseGeometry(mark.Bounds));
                break;
            case MarkKind.Line:
                dc.DrawLine(stroke, a, b);
                break;
            case MarkKind.Arrow:
                Arrow(dc, stroke, a, b);
                break;
            case MarkKind.Pen:
                dc.DrawGeometry(null, stroke, Path(mark.Points, false));
                break;
            case MarkKind.Highlight:
                brush.Opacity = .35;
                dc.DrawGeometry(null, stroke, Path(mark.Points, false));
                break;
            case MarkKind.Text:
                PaintText(dc, mark, brush);
                break;
            case MarkKind.Number:
                if ((b - a).Length > style.Size / 2 + 5)
                    Arrow(dc, new Pen(brush, Math.Max(2, style.Size / 10)), a, b);
                var badge = EditableBounds(mark);
                if (style.Square)
                    dc.DrawRoundedRectangle(brush, null, badge, style.Size / 5, style.Size / 5);
                else
                    dc.DrawEllipse(brush, null, a, style.Size / 2, style.Size / 2);
                string digits = mark.Number.ToString(CultureInfo.InvariantCulture);
                var label = new FormattedText(digits, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    style.Size * Math.Min(.54, 1.35 / digits.Length), Contrast(style.Color), 1);
                dc.DrawText(label, new Point(a.X - label.Width / 2, a.Y - label.Height / 2));
                if (!string.IsNullOrWhiteSpace(mark.Text))
                    PaintText(dc, new(MarkKind.Text, [new(a.X + style.Size / 2 + 5, a.Y - style.Size / 2)],
                        mark.Text, Style: style with { Size = Math.Max(10, Math.Round(style.Size * .55)), Background = true }), brush);
                break;
            case MarkKind.Mosaic:
                var r = Rect.Intersect(mark.Bounds, new Rect(0, 0, Width, Height));
                if (r.IsEmpty || r.Width < 1 || r.Height < 1)
                    return;
                var area = new Int32Rect((int)r.X, (int)r.Y, Math.Max(1, (int)r.Width), Math.Max(1, (int)r.Height));
                var fragment = new CroppedBitmap(Image, area);
                var tiny = new TransformedBitmap(fragment, new ScaleTransform(1.0 / Math.Min(12, area.Width), 1.0 / Math.Min(12, area.Height)));
                var tile = new ImageDrawing(tiny, r);
                var group = new DrawingGroup();
                RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
                group.Children.Add(tile);
                dc.DrawDrawing(group);
                break;
        }
    }

    private void PaintText(DrawingContext dc, ShotMark mark, Brush brush)
    {
        var a = mark.Points[0];
        var text = FormatText(mark, brush);
        var style = mark.Appearance;
        if (style.Background)
            dc.DrawRoundedRectangle(Contrast(style.Color), null, TextBounds(a, text), 4, 4);
        var origin = new Point(a.X + 4, a.Y + 2);
        if (style.Outline)
            dc.DrawGeometry(brush, new Pen(Contrast(style.Color), 2), text.BuildGeometry(origin));
        dc.DrawText(text, origin);
    }

    private FormattedText FormatText(ShotMark mark, Brush brush) =>
        new(mark.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(mark.Appearance.Font), mark.Appearance.Italic ? FontStyles.Italic : FontStyles.Normal,
                mark.Appearance.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            mark.Appearance.Size, brush, 1)
        {
            MaxTextWidth = mark.TextWidth ?? Math.Max(1, Width - mark.Points[0].X - 8)
        };

    private static Rect TextBounds(Point point, FormattedText text) =>
        new(point.X, point.Y, text.WidthIncludingTrailingWhitespace + 8, text.Height + 4);

    private static Brush Contrast(Color color) =>
        .2126 * color.ScR + .7152 * color.ScG + .0722 * color.ScB > .179 ? Brushes.Black : Brushes.White;

    private static void Arrow(DrawingContext dc, Pen pen, Point from, Point to)
    {
        dc.DrawLine(pen, from, to);
        Vector direction = to - from;
        if (direction.Length < 1)
            return;
        double length = Math.Min(direction.Length, 5 * pen.Thickness);
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var wing = to - direction * length;
        dc.DrawGeometry(pen.Brush, null, Path([to, wing + normal * length * .4, wing - normal * length * .4], true));
    }

    private static StreamGeometry Path(IReadOnlyList<Point> points, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], closed, closed);
            for (int i = 1; i < points.Count; i++)
                ctx.LineTo(points[i], true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    // Unmarked PNG exports can keep the source pixels without a premultiplied-alpha render pass.
    public BitmapSource RenderForExport() => marks.Count == 0 ? Image : Render();

    public BitmapSource Render(bool forOcr = false)
    {
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
            Draw(dc, forOcr: forOcr);
        var image = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(drawing);
        image.Freeze();
        return image;
    }
}

internal static class ShotSelection
{
    public static PixelRect Between(Point a, Point b) => new((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Max(a.X, b.X), (int)Math.Max(a.Y, b.Y));
    public static PixelRect Move(PixelRect rect, int dx, int dy, PixelRect bounds) => rect.Offset(Math.Clamp(dx, bounds.Left - rect.Left, bounds.Right - rect.Right), Math.Clamp(dy, bounds.Top - rect.Top, bounds.Bottom - rect.Bottom));
    public static PixelRect AdjustEdge(PixelRect rect, int dLeft, int dTop, int dRight, int dBottom, PixelRect bounds)
    {
        int left = Math.Clamp(rect.Left + dLeft, bounds.Left, rect.Right - 1);
        int top = Math.Clamp(rect.Top + dTop, bounds.Top, rect.Bottom - 1);
        int right = Math.Clamp(rect.Right + dRight, left + 1, bounds.Right);
        int bottom = Math.Clamp(rect.Bottom + dBottom, top + 1, bounds.Bottom);
        return new(left, top, right, bottom);
    }
    public static PixelRect ScaleAroundCenter(PixelRect rect, double factor, PixelRect bounds)
    {
        int nw = Math.Clamp((int)Math.Round(rect.Width * factor), 1, bounds.Width);
        int nh = Math.Clamp((int)Math.Round(rect.Height * factor), 1, bounds.Height);
        int cx = rect.Center.X, cy = rect.Center.Y;
        int left = Math.Clamp(cx - nw / 2, bounds.Left, bounds.Right - nw);
        int top = Math.Clamp(cy - nh / 2, bounds.Top, bounds.Bottom - nh);
        return new(left, top, left + nw, top + nh);
    }
    public static (string Id, Point Point)[] Handles(PixelRect r) => [("nw", new(r.Left, r.Top)), ("n", new(r.Center.X, r.Top)), ("ne", new(r.Right, r.Top)), ("w", new(r.Left, r.Center.Y)), ("e", new(r.Right, r.Center.Y)), ("sw", new(r.Left, r.Bottom)), ("s", new(r.Center.X, r.Bottom)), ("se", new(r.Right, r.Bottom))];
}
