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
    private static readonly Pen Outline = new(new SolidColorBrush(Color.FromRgb(24, 147, 238)), 1.6);
    private static readonly Brush HandleShadow = new SolidColorBrush(Color.FromArgb(90, 12, 20, 28));
    private static readonly Pen GuidePen = new(new SolidColorBrush(Color.FromArgb(145, 24, 147, 238)), 1) { DashStyle = DashStyles.Dash };
    private static readonly Brush LoupeBg = new SolidColorBrush(Color.FromArgb(232, 22, 30, 38));
    private static readonly Pen LoupeBorder = new(new SolidColorBrush(Color.FromArgb(200, 180, 200, 220)), 1);
    static ShotSurface()
    {
        Shade.Freeze();
        Outline.Freeze();
        HandleShadow.Freeze();
        GuidePen.Freeze();
        LoupeBg.Freeze();
        LoupeBorder.Freeze();
    }

    private DesktopFrame? desktop;
    private string? drag;
    private Point anchor;
    private PixelRect before;
    private PixelRect? viewport;
    private PixelPoint pointerPixel = new(-1, -1);
    private bool showLoupe;
    private bool hexColorFormat = true;
    private List<Point>? points;
    private PixelRect hover;
    private ShotMark? pending;
    private ShotMark? selected;
    private int? requestedNumber;
    public ShotDocument Document { get; }
    public DesktopFrame? Desktop => desktop;
    public PixelRect Selection { get; private set; }
    public PixelRect? Viewport
    {
        get => viewport;
        set
        {
            if (viewport == value) return;
            viewport = value;
            InvalidateVisual();
        }
    }
    public bool ShowLoupe
    {
        get => showLoupe;
        set
        {
            if (showLoupe == value) return;
            showLoupe = value;
            InvalidateVisual();
        }
    }
    public bool HexColorFormat
    {
        get => hexColorFormat;
        set
        {
            if (hexColorFormat == value) return;
            hexColorFormat = value;
            if (showLoupe) InvalidateVisual();
        }
    }
    public MarkKind? Tool { get; private set; }
    public ShotMark? Selected => selected;
    public MarkStyle MarkAppearance { get; private set; } = MarkStyle.For(MarkKind.Rectangle);
    public int NextNumber => requestedNumber ?? Document.NextNumber;

    public event Action? Changed;
    public event Action? SelectionDoubleClicked;
    public event Action<Point, ShotMark?>? InlineTextRequested;
    public Func<string, string?>? EnterText { get; init; }

    public ShotSurface(ShotDocument document, DesktopFrame? desktop = null)
    {
        Document = document;
        this.desktop = desktop;
        Focusable = true;
        FocusVisualStyle = null;
        InputMethod.SetIsInputMethodEnabled(this, false);
        InputMethod.SetPreferredImeState(this, InputMethodState.Off);
        ClipToBounds = true;
        Selection = desktop is null ? document.Full : default;
        Cursor = Cursors.Cross;
    }

    public void UpdateDesktop(DesktopFrame next)
    {
        desktop = next;
        Document.ReplaceImage(next.Image);
        Refresh();
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

    public void SetSelection(PixelRect rect)
    {
        if (desktop is null)
            return;
        var next = rect.Intersect(Document.Full);
        if (!next.HasArea)
            return;
        Selection = next;
        Refresh();
    }

    public void AdjustSelectionEdge(int dLeft, int dTop, int dRight, int dBottom)
    {
        if (!Selection.HasArea || desktop is null)
            return;
        Selection = ShotSelection.AdjustEdge(Selection, dLeft, dTop, dRight, dBottom, Document.Full);
        Refresh();
    }

    public void ScaleSelection(int delta)
    {
        if (!Selection.HasArea || desktop is null || delta == 0)
            return;
        Selection = ShotSelection.ScaleAroundCenter(Selection, delta > 0 ? 1.05 : 1 / 1.05, Document.Full);
        Refresh();
    }

    public string? PickColor()
    {
        var p = Pointer();
        if (desktop is null || !Document.Full.Contains(p))
            return null;
        var (r, g, b) = SamplePixel(p.X, p.Y);
        return hexColorFormat ? $"#{r:X2}{g:X2}{b:X2}" : $"rgb({r}, {g}, {b})";
    }

    private (byte R, byte G, byte B) SamplePixel(int x, int y)
    {
        var bytes = new byte[4];
        var source = new FormatConvertedBitmap(new CroppedBitmap(Document.Image, new Int32Rect(x, y, 1, 1)), PixelFormats.Bgr32, null, 0);
        source.CopyPixels(bytes, 4, 0);
        return (bytes[2], bytes[1], bytes[0]);
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

    public void ClearAllMarks()
    {
        if (!Document.HasMarks)
            return;
        selected = null;
        requestedNumber = null;
        Document.ClearMarks();
        Refresh();
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

    private PixelRect ActiveFrame => viewport ?? Document.Full;
    private double ScaleX => ActualWidth / ActiveFrame.Width;
    private double ScaleY => ActualHeight / ActiveFrame.Height;

    private Point Pixel(Point p) => new(Math.Clamp(Math.Round(p.X / ScaleX) + ActiveFrame.Left, 0, Document.Width), Math.Clamp(Math.Round(p.Y / ScaleY) + ActiveFrame.Top, 0, Document.Height));
    private Point View(Point p) => new((p.X - ActiveFrame.Left) * ScaleX, (p.Y - ActiveFrame.Top) * ScaleY);
    public Rect ViewRect(PixelRect r) => new(View(new Point(r.Left, r.Top)), View(new Point(r.Right, r.Bottom)));
    internal Point ToView(Point p) => View(p);
    internal double ViewScaleX => ScaleX;
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var active = ActiveFrame;
        dc.PushTransform(new ScaleTransform(ScaleX, ScaleY));
        if (viewport is { } crop)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, crop.Width, crop.Height)));
            dc.PushTransform(new TranslateTransform(-crop.Left, -crop.Top));
        }
        if (desktop is null)
            Document.Draw(dc, pending, replaces: drag is "edit" || drag?.StartsWith("mark-handle:", StringComparison.Ordinal) == true ? selected : null);
        else
        {
            dc.DrawImage(Document.Image, new Rect(0, 0, Document.Width, Document.Height));
            if (Selection.HasArea)
            {
                dc.PushClip(new RectangleGeometry(new Rect(Selection.Left, Selection.Top, Selection.Width, Selection.Height)));
                Document.Draw(dc, pending, replaces: drag is "edit" || drag?.StartsWith("mark-handle:", StringComparison.Ordinal) == true ? selected : null, drawImage: false);
                dc.Pop();
            }
        }
        if (viewport is not null)
        {
            dc.Pop();
            dc.Pop();
        }
        dc.Pop();
        var activeMark = drag == "duplicate" ? selected : (pending ?? selected);
        if (selected is not null && activeMark is { } mark)
        {
            var markBounds = Document.EditableBounds(mark);
            dc.DrawRectangle(null, Outline, new Rect(View(markBounds.TopLeft), View(markBounds.BottomRight)));
            foreach (var (_, hp) in ShotDocument.MarkHandles(mark))
            {
                var hv = View(hp);
                dc.DrawEllipse(HandleShadow, null, hv, 5.2, 5.2);
                dc.DrawEllipse(Brushes.White, Outline, hv, 4.0, 4.0);
            }
        }
        if (desktop is null)
            return;
        var bounds = new Rect(RenderSize);
        var r = Selection.HasArea ? Selection : hover;
        if (!r.HasArea)
        {
            dc.DrawRectangle(Shade, null, bounds);
        }
        else
        {
            var rect = ViewRect(r);
            var mask = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), new RectangleGeometry(rect));
            dc.DrawGeometry(Shade, null, mask);
            dc.DrawRectangle(null, Outline, rect);
            if (Selection.HasArea)
                foreach (var h in ShotSelection.Handles(r))
                {
                    var v = View(h.Point);
                    dc.DrawEllipse(HandleShadow, null, v, 5.2, 5.2);
                    dc.DrawEllipse(Brushes.White, Outline, v, 4.0, 4.0);
                }
        }

        if (showLoupe && Document.Full.Contains(pointerPixel))
            DrawLoupeAndGuides(dc, bounds, r);
    }

    private void DrawLoupeAndGuides(DrawingContext dc, Rect bounds, PixelRect sel)
    {
        var pv = View(new Point(pointerPixel.X + 0.5, pointerPixel.Y + 0.5));
        dc.DrawLine(GuidePen, new Point(0, pv.Y), new Point(bounds.Width, pv.Y));
        dc.DrawLine(GuidePen, new Point(pv.X, 0), new Point(pv.X, bounds.Height));
        if (sel.HasArea)
        {
            var cv = View(new Point(sel.Center.X, sel.Center.Y));
            dc.DrawLine(GuidePen, new Point(cv.X, sel.Top * ScaleY), new Point(cv.X, sel.Bottom * ScaleY));
            dc.DrawLine(GuidePen, new Point(sel.Left * ScaleX, cv.Y), new Point(sel.Right * ScaleX, cv.Y));
        }

        const int patch = 11, boxSize = 110, infoH = 44;
        var monitor = desktop!.MonitorAt(pointerPixel);
        var mView = ViewRect(monitor);
        double lx = pv.X + 18 + boxSize <= mView.Right - 8 ? pv.X + 18 : pv.X - boxSize - 18;
        double ly = pv.Y + 18 + boxSize + infoH <= mView.Bottom - 8 ? pv.Y + 18 : pv.Y - boxSize - infoH - 18;
        lx = Math.Clamp(lx, mView.Left + 6, Math.Max(mView.Left + 6, mView.Right - boxSize - 6));
        ly = Math.Clamp(ly, mView.Top + 6, Math.Max(mView.Top + 6, mView.Bottom - boxSize - infoH - 6));

        var outerRect = new Rect(lx, ly, boxSize, boxSize + infoH);
        dc.DrawRoundedRectangle(LoupeBg, LoupeBorder, outerRect, 6, 6);

        int half = patch / 2;
        int sx0 = Math.Clamp(pointerPixel.X - half, 0, Math.Max(0, Document.Width - patch));
        int sy0 = Math.Clamp(pointerPixel.Y - half, 0, Math.Max(0, Document.Height - patch));
        int sw = Math.Min(patch, Document.Width - sx0);
        int sh = Math.Min(patch, Document.Height - sy0);
        if (sw > 0 && sh > 0)
        {
            var srcRect = new Int32Rect(sx0, sy0, sw, sh);
            var crop = new CroppedBitmap(Document.Image, srcRect);
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
            group.Children.Add(new ImageDrawing(crop, new Rect(lx + 1, ly + 1, boxSize - 2, boxSize - 2)));
            dc.DrawDrawing(group);
        }

        double cell = (boxSize - 2.0) / patch;
        int relX = Math.Clamp(pointerPixel.X - sx0, 0, patch - 1);
        int relY = Math.Clamp(pointerPixel.Y - sy0, 0, patch - 1);
        dc.DrawRectangle(null, Outline, new Rect(lx + 1 + relX * cell, ly + 1 + relY * cell, cell, cell));

        var (r, g, b) = SamplePixel(pointerPixel.X, pointerPixel.Y);
        string colorText = hexColorFormat ? $"#{r:X2}{g:X2}{b:X2}" : $"{r}, {g}, {b}";
        string line1 = $"{pointerPixel.X + desktop.Bounds.Left}, {pointerPixel.Y + desktop.Bounds.Top} · {colorText}";
        var t1 = new FormattedText(line1, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei UI"), 11, Brushes.White, 1);
        var t2 = new FormattedText("C 复制颜色 · Shift 切格式", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei UI"), 10, Brushes.LightGray, 1);
        dc.DrawText(t1, new Point(lx + 7, ly + boxSize + 5));
        dc.DrawText(t2, new Point(lx + 7, ly + boxSize + 22));
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

    private string? MarkHandleAt(Point view)
    {
        if (selected is not { } mark)
            return null;
        foreach (var (id, p) in ShotDocument.MarkHandles(mark))
            if ((View(p) - view).Length <= 8)
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
            if (MarkHandleAt(view) is { } markHandle)
            {
                drag = "mark-handle:" + markHandle;
                CaptureMouse();
                Refresh();
                return;
            }
            selected = Document.EditableAt(kind, anchor);
            if (selected is { } existing)
            {
                MarkAppearance = existing.Appearance;
                if (kind is MarkKind.Text or MarkKind.Number && e.ClickCount == 2)
                {
                    if (InlineTextRequested is not null)
                        InlineTextRequested(existing.Points[0], existing);
                    else
                        EditText();
                    return;
                }
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && existing.Kind is not (MarkKind.Pen or MarkKind.Highlight))
                {
                    drag = "duplicate";
                    pending = existing with { Number = existing.Kind == MarkKind.Number ? NextNumber : existing.Number };
                }
                else
                    drag = "edit";
                Refresh();
            }
            else if (kind == MarkKind.Text)
            {
                if (InlineTextRequested is not null)
                {
                    Refresh();
                    InlineTextRequested(anchor, null);
                    return;
                }
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
            if (Tool is null && Selection.Contains(local) && handle is null && e.ClickCount == 2)
            {
                SelectionDoubleClicked?.Invoke();
                return;
            }
            drag = handle ?? (Selection.Contains(local) ? "move" : "new");
        }
        if (drag is not null)
            CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point view = e.GetPosition(this), p = Pixel(view);
        pointerPixel = new PixelPoint((int)p.X, (int)p.Y);
        var mods = Keyboard.Modifiers;
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
            else if (desktop is not null && showLoupe)
                InvalidateVisual();

            Cursor = HandleAt(view) switch
            {
                "n" or "s" => Cursors.SizeNS,
                "e" or "w" => Cursors.SizeWE,
                "ne" or "sw" => Cursors.SizeNESW,
                "nw" or "se" => Cursors.SizeNWSE,
                _ => MarkHandleAt(view) switch
                {
                    "nw" or "se" => Cursors.SizeNWSE,
                    "ne" or "sw" => Cursors.SizeNESW,
                    "start" or "end" => Cursors.SizeAll,
                    _ => Tool is null && desktop is not null && Selection.Contains(new((int)p.X, (int)p.Y)) ? Cursors.SizeAll : Cursors.Cross
                }
            };
            return;
        }

        if (drag is "edit" or "duplicate" && selected is { } move)
        {
            var delta = p - anchor;
            if (mods.HasFlag(ModifierKeys.Shift))
            {
                if (Math.Abs(delta.X) >= Math.Abs(delta.Y)) delta.Y = 0;
                else delta.X = 0;
            }
            var origins = move.Points[0];
            double dx = Math.Clamp(delta.X, Selection.Left - origins.X, Selection.Right - 1 - origins.X);
            double dy = Math.Clamp(delta.Y, Selection.Top - origins.Y, Selection.Bottom - 1 - origins.Y);
            pending = delta.Length < 2
                ? null
                : move.Offset(dx, dy) with { Number = drag == "duplicate" && move.Kind == MarkKind.Number ? NextNumber : move.Number };
            if (pending is { Kind: MarkKind.Text } text)
                pending = text with { TextWidth = TextWidth(text.Points[0]) };
        }
        else if (drag.StartsWith("mark-handle:", StringComparison.Ordinal) && selected is { } shape)
        {
            var target = new Point(Math.Clamp(p.X, Selection.Left, Selection.Right), Math.Clamp(p.Y, Selection.Top, Selection.Bottom));
            pending = ShotDocument.ResizeMark(shape, drag["mark-handle:".Length..], target);
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
        {
            if (mods.HasFlag(ModifierKeys.Shift))
            {
                double side = Math.Max(Math.Abs(p.X - anchor.X), Math.Abs(p.Y - anchor.Y));
                p = new Point(
                    Math.Clamp(anchor.X + (p.X >= anchor.X ? side : -side), 0, Document.Width),
                    Math.Clamp(anchor.Y + (p.Y >= anchor.Y ? side : -side), 0, Document.Height));
            }
            Selection = ShotSelection.Between(anchor, p);
        }
        else if (drag == "move")
        {
            int dx = (int)(p.X - anchor.X), dy = (int)(p.Y - anchor.Y);
            if (mods.HasFlag(ModifierKeys.Shift))
            {
                if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
                else dx = 0;
            }
            Selection = ShotSelection.Move(before, dx, dy, Document.Full);
        }
        else
        {
            if (mods.HasFlag(ModifierKeys.Alt))
            {
                double cx = (before.Left + before.Right) / 2.0, cy = (before.Top + before.Bottom) / 2.0;
                double halfW = drag.Contains('w') || drag.Contains('e') ? Math.Abs(p.X - cx) : before.Width / 2.0;
                double halfH = drag.Contains('n') || drag.Contains('s') ? Math.Abs(p.Y - cy) : before.Height / 2.0;
                double maxW = Math.Min(cx, Document.Width - cx);
                double maxH = Math.Min(cy, Document.Height - cy);
                halfW = Math.Clamp(halfW, 0.5, maxW);
                halfH = Math.Clamp(halfH, 0.5, maxH);
                Selection = ShotSelection.Between(new(cx - halfW, cy - halfH), new(cx + halfW, cy + halfH));
            }
            else if (mods.HasFlag(ModifierKeys.Shift) && (drag.Contains('n') || drag.Contains('s')) && (drag.Contains('w') || drag.Contains('e')))
            {
                double fx = drag.Contains('w') ? before.Right : before.Left;
                double fy = drag.Contains('n') ? before.Bottom : before.Top;
                double maxSide = Math.Min(drag.Contains('w') ? fx : Document.Width - fx, drag.Contains('n') ? fy : Document.Height - fy);
                double side = Math.Clamp(Math.Max(Math.Abs(p.X - fx), Math.Abs(p.Y - fy)), 1, Math.Max(1, maxSide));
                Selection = ShotSelection.Between(new(fx, fy), new(drag.Contains('w') ? fx - side : fx + side, drag.Contains('n') ? fy - side : fy + side));
            }
            else
            {
                Point a = new(drag.Contains('w') ? p.X : before.Left, drag.Contains('n') ? p.Y : before.Top);
                Point b = new(drag.Contains('e') ? p.X : before.Right, drag.Contains('s') ? p.Y : before.Bottom);
                Selection = ShotSelection.Between(a, b);
            }
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
            if ((drag == "edit" || drag.StartsWith("mark-handle:", StringComparison.Ordinal)) && selected is { } old)
            {
                Document.Replace(old, mark);
                selected = mark;
            }
            else if (drag == "duplicate")
            {
                Document.Add(mark);
                selected = mark;
                if (mark.Kind == MarkKind.Number && requestedNumber is not null)
                    requestedNumber = Math.Min(9999, mark.Number + 1);
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

    public void CommitInlineText(Point textAnchor, ShotMark? existing, string text)
    {
        if (existing is { } original)
        {
            selected = original;
            ReplaceSelected(original.Kind == MarkKind.Text && string.IsNullOrWhiteSpace(text)
                ? null
                : original with
                {
                    Text = original.Kind == MarkKind.Number ? text.Trim() : text,
                    Style = original.Kind == MarkKind.Text ? MarkAppearance : original.Appearance,
                    TextWidth = original.Kind == MarkKind.Text ? TextWidth(original.Points[0]) : original.TextWidth
                });
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            selected = new(MarkKind.Text, [textAnchor], text, Style: MarkAppearance, TextWidth: TextWidth(textAnchor));
            Document.Add(selected);
            Refresh();
        }
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
