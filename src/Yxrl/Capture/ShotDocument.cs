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
    Mosaic
}

internal sealed record ShotMark(MarkKind Kind, Point[] Points, string Text = "")
{
    public Rect Bounds => new(Points[0], Points[^1]);

    public ShotMark Offset(double x, double y) => this with
    {
        Points = Points.Select(p => new Point(p.X + x, p.Y + y)).ToArray()
    };
}

internal sealed class ShotDocument
{
    private static readonly Pen Stroke = new(new SolidColorBrush(Color.FromRgb(212, 64, 47)), 3)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round
    };
    static ShotDocument()
    {
        Stroke.Freeze();
    }

    public BitmapSource Image { get; }
    public int Width => Image.PixelWidth;
    public int Height => Image.PixelHeight;
    public PixelRect Full => new(0, 0, Width, Height);

    private readonly List<ShotMark> marks = [];
    private readonly Stack<ShotMark> redo = new();
    public bool CanUndo => marks.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public ShotDocument(BitmapSource image)
    {
        Image = image;
        if (!image.IsFrozen && image.CanFreeze)
            image.Freeze();
    }

    public void Add(ShotMark mark)
    {
        marks.Add(mark);
        redo.Clear();
    }

    public void Undo()
    {
        if (CanUndo)
        {
            redo.Push(marks[^1]);
            marks.RemoveAt(marks.Count - 1);
        }
    }

    public void Redo()
    {
        if (redo.TryPop(out var mark))
            marks.Add(mark);
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

    public void Draw(DrawingContext dc, ShotMark? pending = null, bool forOcr = false)
    {
        dc.DrawImage(Image, new Rect(0, 0, Width, Height));
        foreach (var mark in marks)
            Paint(dc, mark);
        if (pending is not null)
            Paint(dc, pending);
        if (forOcr)
            foreach (var mark in marks.Where(m => m.Kind == MarkKind.Mosaic))
                dc.DrawRectangle(Brushes.Black, null, mark.Bounds);
    }

    private void Paint(DrawingContext dc, ShotMark mark)
    {
        var a = mark.Points[0];
        var b = mark.Points[^1];
        switch (mark.Kind)
        {
            case MarkKind.Rectangle:
                dc.DrawRectangle(null, Stroke, mark.Bounds);
                break;
            case MarkKind.Arrow:
                dc.DrawLine(Stroke, a, b);
                Vector direction = b - a;
                if (direction.Length < 1)
                    return;
                double length = Math.Min(direction.Length, 15);
                direction.Normalize();
                var normal = new Vector(-direction.Y, direction.X);
                var wing = b - direction * length;
                dc.DrawGeometry(Stroke.Brush, null, Path([b, wing + normal * length * .4, wing - normal * length * .4], true));
                break;
            case MarkKind.Pen:
                dc.DrawGeometry(null, Stroke, Path(mark.Points, false));
                break;
            case MarkKind.Text:
                var text = new FormattedText(mark.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Microsoft YaHei UI"), 20, Stroke.Brush, 1);
                dc.DrawText(text, a);
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
    public static (string Id, Point Point)[] Handles(PixelRect r) => [("nw", new(r.Left, r.Top)), ("n", new(r.Center.X, r.Top)), ("ne", new(r.Right, r.Top)), ("w", new(r.Left, r.Center.Y)), ("e", new(r.Right, r.Center.Y)), ("sw", new(r.Left, r.Bottom)), ("s", new(r.Center.X, r.Bottom)), ("se", new(r.Right, r.Bottom))];
}
