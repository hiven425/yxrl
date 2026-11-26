using System.Windows;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed class ShotSurface : FrameworkElement
{
    private static readonly Brush Shade = new SolidColorBrush(Color.FromArgb(120, 16, 24, 32));
    private static readonly Pen Outline = new(new SolidColorBrush(Color.FromRgb(24, 147, 238)), 1.5);
    static ShotSurface()
    {
        Shade.Freeze();
        Outline.Freeze();
    }

    private readonly DesktopFrame? desktop;
    private string? drag;
    private Point anchor;
    private PixelRect before;
    private List<Point>? points;
    private PixelRect hover;
    private ShotMark? pending;
    public ShotDocument Document { get; }
    public PixelRect Selection { get; private set; }
    public MarkKind? Tool { get; set; }

    public event Action? Changed;
    public Func<Point, string?>? EnterText { get; init; }

    public ShotSurface(ShotDocument document, DesktopFrame? desktop = null)
    {
        Document = document;
        this.desktop = desktop;
        Focusable = true;
        ClipToBounds = true;
        Selection = desktop is null ? document.Full : default;
        Cursor = Cursors.Cross;
    }

    public void SelectWindowAtPointer()
    {
        if (desktop is null)
            return;
        var local = Native.GetCursorPos(out var p) ? new PixelPoint(p.X - desktop.Bounds.Left, p.Y - desktop.Bounds.Top) : new PixelPoint(0, 0);
        SelectWindowAt(local);
    }

    internal void SelectWindowAt(PixelPoint local)
    {
        if (desktop is null)
            return;
        Selection = desktop.WindowAt(local);
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SurfacePeer(this);
    private sealed class SurfacePeer(ShotSurface owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override string GetClassNameCore() => nameof(ShotSurface);
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Invoke && owner.desktop is not null ? this : base.GetPattern(pattern);
        public void Invoke() => owner.Dispatcher.InvokeAsync(owner.SelectWindowAtPointer);
    }

    private double ScaleX => ActualWidth / Document.Width;
    private double ScaleY => ActualHeight / Document.Height;

    private Point Pixel(Point p) => new(Math.Clamp(Math.Round(p.X / ScaleX), 0, Document.Width), Math.Clamp(Math.Round(p.Y / ScaleY), 0, Document.Height));
    private Point View(Point p) => new(p.X * ScaleX, p.Y * ScaleY);
    public Rect ViewRect(PixelRect r) => new(View(new Point(r.Left, r.Top)), View(new Point(r.Right, r.Bottom)));
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;
        dc.PushTransform(new ScaleTransform(ScaleX, ScaleY));
        Document.Draw(dc, pending);
        dc.Pop();
        if (desktop is null)
            return;
        var bounds = new Rect(RenderSize);
        var r = Selection.HasArea ? Selection : hover;
        if (!r.HasArea)
        {
            dc.DrawRectangle(Shade, null, bounds);
            return;
        }

        var rect = ViewRect(r);
        var mask = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), new RectangleGeometry(rect));
        dc.DrawGeometry(Shade, null, mask);
        dc.DrawRectangle(null, Outline, rect);
        if (Selection.HasArea)
            foreach (var h in ShotSelection.Handles(r))
            {
                var v = View(h.Point);
                dc.DrawRectangle(Brushes.White, Outline, new Rect(v.X - 3, v.Y - 3, 6, 6));
            }
    }

    public void Refresh()
    {
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void Nudge(int dx, int dy)
    {
        if (!Selection.HasArea || desktop is null)
            return;
        Selection = ShotSelection.Move(Selection, dx, dy, Document.Full);
        Refresh();
    }

    private string? HandleAt(Point view)
    {
        if (!Selection.HasArea || desktop is null)
            return null;
        foreach (var (id, p) in ShotSelection.Handles(Selection))
            if ((View(p) - view).Length <= 7)
                return id;
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var view = e.GetPosition(this);
        anchor = Pixel(view);
        before = Selection;
        var local = new PixelPoint((int)anchor.X, (int)anchor.Y);
        var handle = HandleAt(view);
        Focus();
        e.Handled = true;
        if (Tool is { } kind && Selection.Contains(local) && handle is null)
        {
            if (kind == MarkKind.Text)
            {
                var label = EnterText?.Invoke(view);
                if (!string.IsNullOrWhiteSpace(label))
                    Document.Add(new(kind, [anchor], label));
                Refresh();
                return;
            }

            drag = "mark";
            points = [anchor, anchor];
            pending = new(kind, points.ToArray());
        }
        else if (desktop is not null)
            drag = handle ?? (Selection.Contains(local) ? "move" : "new");
        if (drag is not null)
            CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point view = e.GetPosition(this), p = Pixel(view);
        if (drag is null)
        {
            if (desktop is not null && !Selection.HasArea)
            {
                var hit = desktop.WindowAt(new((int)p.X, (int)p.Y));
                if (hit != hover)
                {
                    hover = hit;
                    InvalidateVisual();
                }
            }

            Cursor = HandleAt(view) switch
            {
                "n" or "s" => Cursors.SizeNS,
                "e" or "w" => Cursors.SizeWE,
                "ne" or "sw" => Cursors.SizeNESW,
                "nw" or "se" => Cursors.SizeNWSE,
                _ => Tool is null && desktop is not null && Selection.Contains(new((int)p.X, (int)p.Y)) ? Cursors.SizeAll : Cursors.Cross
            };
            return;
        }

        if (drag == "mark" && points is not null && Tool is { } kind)
        {
            p = new Point(Math.Clamp(p.X, Selection.Left, Selection.Right), Math.Clamp(p.Y, Selection.Top, Selection.Bottom));
            if (kind == MarkKind.Pen)
                points.Add(p);
            else
                points[^1] = p;
            pending = new(kind, points.ToArray());
        }
        else if (drag == "new")
            Selection = ShotSelection.Between(anchor, p);
        else if (drag == "move")
            Selection = ShotSelection.Move(before, (int)(p.X - anchor.X), (int)(p.Y - anchor.Y), Document.Full);
        else
        {
            Point a = new(drag.Contains('w') ? p.X : before.Left, drag.Contains('n') ? p.Y : before.Top);
            Point b = new(drag.Contains('e') ? p.X : before.Right, drag.Contains('s') ? p.Y : before.Bottom);
            Selection = ShotSelection.Between(a, b);
        }

        Refresh();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (drag is null)
            return;
        if (drag == "new" && (!Selection.HasArea || (Pixel(e.GetPosition(this)) - anchor).Length < 3) && desktop is not null)
            SelectWindowAt(new((int)anchor.X, (int)anchor.Y));
        if (pending is not null)
            Document.Add(pending);
        drag = null;
        points = null;
        pending = null;
        ReleaseMouseCapture();
        Refresh();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (drag is not null)
        {
            drag = null;
            points = null;
            pending = null;
            Selection = before;
            Refresh();
        }

        base.OnLostMouseCapture(e);
    }
}
