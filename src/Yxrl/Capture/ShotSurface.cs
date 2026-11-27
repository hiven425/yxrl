using System.Windows;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private ShotMark? selected;
    private int? requestedNumber;
    public ShotDocument Document { get; }
    public PixelRect Selection { get; private set; }
    public MarkKind? Tool { get; private set; }
    public ShotMark? Selected => selected;
    public MarkStyle MarkAppearance { get; private set; } = MarkStyle.For(MarkKind.Rectangle);
    public int NextNumber => requestedNumber ?? Document.NextNumber;

    public event Action? Changed;
    public Func<string, string?>? EnterText { get; init; }

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
        SelectWindowAt(Pointer());
    }

    private PixelPoint Pointer() => desktop is not null && Native.GetCursorPos(out var point)
        ? new(point.X - desktop.Bounds.Left, point.Y - desktop.Bounds.Top) : new(0, 0);

    internal void SelectWindowAt(PixelPoint local)
    {
        if (desktop is null)
            return;
        Selection = desktop.WindowAt(local);
        Refresh();
    }

    public void SelectScreen(bool all)
    {
        if (desktop is null)
            return;
        Selection = all ? Document.Full : desktop.MonitorAt(Pointer());
        Refresh();
    }

    public string? PickColor()
    {
        var p = Pointer();
        if (desktop is null || !Document.Full.Contains(p))
            return null;
        var bytes = new byte[4];
        var source = new FormatConvertedBitmap(new CroppedBitmap(Document.Image, new Int32Rect(p.X, p.Y, 1, 1)), PixelFormats.Bgr32, null, 0);
        source.CopyPixels(bytes, 4, 0);
        return $"#{bytes[2]:X2}{bytes[1]:X2}{bytes[0]:X2}";
    }

    public void UseTool(MarkKind? tool)
    {
        Tool = tool;
        selected = null;
        if (tool is { } kind)
            MarkAppearance = MarkStyle.For(kind);
        Refresh();
    }

    public void SetStyle(MarkStyle value)
    {
        MarkAppearance = value;
        if (selected is { } original)
            ReplaceSelected(original with { Style = value });
        else
            Refresh();
    }

    public void SetNumber(int value)
    {
        if (selected is { Kind: MarkKind.Number } number)
            ReplaceSelected(number with { Number = value });
        else
        {
            requestedNumber = value;
            Refresh();
        }
    }

    private void ReplaceSelected(ShotMark? value)
    {
        if (selected is { } original)
            Document.Replace(original, value);
        selected = value;
        Refresh();
    }

    public bool ClearSelected(bool delete = false)
    {
        if (selected is null)
            return false;
        if (delete)
            ReplaceSelected(null);
        else
        {
            selected = null;
            Refresh();
        }
        return true;
    }

    public void History(bool forward)
    {
        selected = null;
        requestedNumber = null;
        if (forward) Document.Redo();
        else Document.Undo();
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
        if (desktop is null)
            Document.Draw(dc, pending, replaces: drag == "edit" ? selected : null);
        else
        {
            dc.DrawImage(Document.Image, new Rect(0, 0, Document.Width, Document.Height));
            if (Selection.HasArea)
            {
                dc.PushClip(new RectangleGeometry(new Rect(Selection.Left, Selection.Top, Selection.Width, Selection.Height)));
                Document.Draw(dc, pending, replaces: drag == "edit" ? selected : null, drawImage: false);
                dc.Pop();
            }
        }
        dc.Pop();
        if (selected is { } mark)
        {
            var markBounds = Document.EditableBounds(pending ?? mark);
            dc.DrawRectangle(null, Outline, new Rect(View(markBounds.TopLeft), View(markBounds.BottomRight)));
        }
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
            selected = Document.EditableAt(kind, anchor);
            if (selected is { } existing)
            {
                MarkAppearance = existing.Appearance;
                if (e.ClickCount == 2)
                {
                    EditText();
                    return;
                }
                drag = "edit";
                Refresh();
            }
            else if (kind == MarkKind.Text)
            {
                var text = EnterText?.Invoke("");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    selected = new(kind, [anchor], text, Style: MarkAppearance, TextWidth: TextWidth(anchor));
                    Document.Add(selected);
                }
                Refresh();
                return;
            }
            else
            {
                drag = "mark";
                points = [anchor, anchor];
                pending = new(kind, points.ToArray(), Number: kind == MarkKind.Number ? NextNumber : 0, Style: MarkAppearance);
            }
        }
        else if (desktop is not null)
        {
            selected = null;
            drag = handle ?? (Selection.Contains(local) ? "move" : "new");
        }
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

        if (drag == "edit" && selected is { } move)
        {
            var delta = p - anchor;
            var origins = move.Points[0];
            pending = delta.Length < 2 ? null : move.Offset(
                Math.Clamp(delta.X, Selection.Left - origins.X, Selection.Right - 1 - origins.X),
                Math.Clamp(delta.Y, Selection.Top - origins.Y, Selection.Bottom - 1 - origins.Y));
            if (pending is { Kind: MarkKind.Text } text)
                pending = text with { TextWidth = TextWidth(text.Points[0]) };
        }
        else if (drag == "mark" && points is not null && pending is { } original)
        {
            p = new Point(Math.Clamp(p.X, Selection.Left, Selection.Right), Math.Clamp(p.Y, Selection.Top, Selection.Bottom));
            if (original.Kind is MarkKind.Pen or MarkKind.Highlight)
                points.Add(p);
            else
                points[^1] = p;
            pending = original with { Points = points.ToArray() };
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
        if (pending is { } mark)
        {
            if (drag == "edit" && selected is { } old)
            {
                Document.Replace(old, mark);
                selected = mark;
            }
            else
            {
                Document.Add(mark);
                if (mark.Kind == MarkKind.Number && requestedNumber is not null)
                    requestedNumber = Math.Min(9999, mark.Number + 1);
            }
        }
        drag = null;
        points = null;
        pending = null;
        ReleaseMouseCapture();
        Refresh();
        e.Handled = true;
    }

    public void EditText()
    {
        if (selected is not { Kind: MarkKind.Text or MarkKind.Number } mark)
            return;
        var text = EnterText?.Invoke(mark.Text);
        if (text is not null)
            ReplaceSelected(mark.Kind == MarkKind.Text && string.IsNullOrWhiteSpace(text) ? null : mark with { Text = text });
    }

    private double TextWidth(Point point) => Math.Max(1, Selection.Right - point.X - 8);

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
