using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal enum LiveAnnotationTool { Pen, Arrow, Rectangle, Number }

internal sealed class RecordingAnnotationOverlay : Window
{
    internal static readonly Color[] Palette =
    [
        Color.FromRgb(0xE0, 0x31, 0x31),
        Color.FromRgb(0x0B, 0x75, 0xD1),
        Color.FromRgb(0x2B, 0x8A, 0x3E),
        Color.FromRgb(0xF0, 0x8C, 0x00)
    ];

    private sealed record AnnotationMark(LiveAnnotationTool Tool, Color Color, double Thickness, List<Point> Points, int Number = 0);

    private readonly PixelRect area;
    private readonly AnnotationSurface surface;
    private readonly List<AnnotationMark> marks = [];
    private AnnotationMark? active;
    private int nextNumber = 1;
    private bool closed, drawingMode, positioning;

    internal LiveAnnotationTool CurrentTool { get; set; } = LiveAnnotationTool.Pen;
    internal Color CurrentColor { get; set; } = Palette[0];
    internal bool PassThrough => !drawingMode;
    internal bool IsDrawing => drawingMode;
    internal int MarkCount => marks.Count;
    internal bool HasMarks => marks.Count > 0;
    internal bool CaptureExcluded => false;

    internal event Action? MarksChanged;
    internal event Action<bool>? DrawingModeChanged;

    public RecordingAnnotationOverlay(PixelRect area)
    {
        this.area = area;
        Title = "YXRL 录制标注层";
        AutomationProperties.SetAutomationId(this, "YxrlRecordingAnnotationOverlay");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        surface = new AnnotationSurface(this);
        Content = surface;

        SourceInitialized += (_, _) =>
        {
            ApplyWindowFlags();
            PositionOverlay();
        };
        Loaded += (_, _) => PositionOverlay();
        DpiChanged += (_, _) => PositionOverlay();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && drawingMode)
            {
                e.Handled = true;
                SetDrawingMode(false);
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && drawingMode)
            {
                e.Handled = true;
                Undo();
            }
        };
        Closed += (_, _) => closed = true;
    }

    internal void SetDrawingMode(bool drawing)
    {
        if (closed)
            return;
        if (drawingMode == drawing && (drawing ? IsVisible : (marks.Count > 0 ? IsVisible : !IsVisible)))
            return;

        drawingMode = drawing;
        active = null;
        if (IsLoaded)
            ApplyWindowFlags();
        SyncVisibility(activateWhenDrawing: drawing);
        surface.UpdateHitSurface(drawing);
        surface.InvalidateVisual();
        DrawingModeChanged?.Invoke(drawingMode);
    }

    internal void Undo()
    {
        if (closed || marks.Count == 0)
            return;
        marks.RemoveAt(marks.Count - 1);
        RecalculateNextNumber();
        surface.InvalidateVisual();
        SyncVisibility(activateWhenDrawing: false);
        MarksChanged?.Invoke();
    }

    internal void Clear()
    {
        if (closed || (marks.Count == 0 && active is null))
            return;
        active = null;
        marks.Clear();
        nextNumber = 1;
        surface.InvalidateVisual();
        SyncVisibility(activateWhenDrawing: false);
        MarksChanged?.Invoke();
    }

    internal void CloseSafely()
    {
        if (closed)
            return;
        closed = true;
        active = null;
        marks.Clear();
        Close();
    }

    internal void BeginStroke(Point point)
    {
        if (closed || !drawingMode)
            return;
        if (CurrentTool == LiveAnnotationTool.Number)
        {
            marks.Add(new AnnotationMark(LiveAnnotationTool.Number, CurrentColor, 2.8, [point], nextNumber++));
            surface.InvalidateVisual();
            MarksChanged?.Invoke();
            return;
        }
        active = new AnnotationMark(CurrentTool, CurrentColor, CurrentTool == LiveAnnotationTool.Pen ? 3.2 : 2.8, [point]);
        surface.InvalidateVisual();
    }

    internal void MoveStroke(Point point)
    {
        if (closed || !drawingMode || active is null)
            return;
        if (active.Tool == LiveAnnotationTool.Pen)
        {
            var prev = active.Points[^1];
            if ((point - prev).Length >= 2.0)
                active.Points.Add(point);
        }
        else if (active.Points.Count == 1)
        {
            active.Points.Add(point);
        }
        else
        {
            active.Points[1] = point;
        }
        surface.InvalidateVisual();
    }

    internal void EndStroke()
    {
        if (closed || active is null)
            return;
        var finished = active;
        active = null;
        bool valid = finished.Tool == LiveAnnotationTool.Pen
            ? finished.Points.Count >= 2 || (finished.Points.Count == 1)
            : finished.Points.Count >= 2 && (finished.Points[1] - finished.Points[0]).Length >= 4;
        if (valid)
        {
            if (finished.Tool == LiveAnnotationTool.Pen && finished.Points.Count == 1)
                finished.Points.Add(new Point(finished.Points[0].X + 0.5, finished.Points[0].Y + 0.5));
            marks.Add(finished);
            MarksChanged?.Invoke();
        }
        surface.InvalidateVisual();
    }

    private void RecalculateNextNumber()
    {
        int max = 0;
        foreach (var mark in marks)
        {
            if (mark.Tool == LiveAnnotationTool.Number && mark.Number > max)
                max = mark.Number;
        }
        nextNumber = max + 1;
    }

    private void SyncVisibility(bool activateWhenDrawing)
    {
        if (closed)
            return;
        if (drawingMode)
        {
            if (!IsVisible)
                Show();
            ApplyWindowFlags();
            PositionOverlay();
            if (activateWhenDrawing)
                Activate();
        }
        else if (marks.Count > 0)
        {
            if (!IsVisible)
                Show();
            ApplyWindowFlags();
            PositionOverlay();
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    private void ApplyWindowFlags()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
            return;
        const int gwlExStyle = -20;
        var style = Native.GetStyle(hwnd, gwlExStyle);
        style |= Native.WsExToolWindow;
        if (drawingMode)
            style &= ~(Native.WsExTransparent | Native.WsExNoActivate);
        else
            style |= Native.WsExTransparent | Native.WsExNoActivate;
        Native.SetStyle(hwnd, gwlExStyle, style);
        IsHitTestVisible = drawingMode;
    }

    private void PositionOverlay()
    {
        if (closed || positioning)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
            return;
        positioning = true;
        try
        {
            Native.SetWindowPos(hwnd, Native.Topmost, area.Left, area.Top, 1, 1, Native.NoActivate | Native.NoOwnerZOrder);
            double dpiScale = Math.Max(1, Native.GetDpiForWindow(hwnd) / 96.0);
            Width = area.Width / dpiScale;
            Height = area.Height / dpiScale;
            Native.SetWindowPos(hwnd, Native.Topmost, area.Left, area.Top, area.Width, area.Height,
                Native.NoActivate | Native.NoOwnerZOrder);
        }
        finally
        {
            positioning = false;
        }
    }

    private sealed class AnnotationSurface : FrameworkElement
    {
        private static readonly Brush HitSurface = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        private readonly RecordingAnnotationOverlay owner;
        private Brush background = Brushes.Transparent;

        static AnnotationSurface()
        {
            HitSurface.Freeze();
        }

        public AnnotationSurface(RecordingAnnotationOverlay owner)
        {
            this.owner = owner;
        }

        public void UpdateHitSurface(bool drawing)
        {
            background = drawing ? HitSurface : Brushes.Transparent;
            Cursor = drawing ? Cursors.Cross : Cursors.Arrow;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!owner.drawingMode)
                return;
            e.Handled = true;
            CaptureMouse();
            owner.BeginStroke(e.GetPosition(this));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!owner.drawingMode || !IsMouseCaptured)
                return;
            owner.MoveStroke(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!IsMouseCaptured)
                return;
            e.Handled = true;
            owner.MoveStroke(e.GetPosition(this));
            ReleaseMouseCapture();
            owner.EndStroke();
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (!owner.drawingMode)
                return;
            e.Handled = true;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            owner.SetDrawingMode(false);
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (var mark in owner.marks)
                DrawMark(dc, mark, dpi);
            if (owner.active is { } cur)
                DrawMark(dc, cur, dpi);
        }

        private static void DrawMark(DrawingContext dc, AnnotationMark mark, double dpi)
        {
            var brush = new SolidColorBrush(mark.Color);
            brush.Freeze();
            var pen = new Pen(brush, mark.Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();

            switch (mark.Tool)
            {
                case LiveAnnotationTool.Pen when mark.Points.Count >= 2:
                {
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(mark.Points[0], false, false);
                        ctx.PolyLineTo(mark.Points.Skip(1).ToList(), true, true);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(null, pen, geo);
                    break;
                }
                case LiveAnnotationTool.Rectangle when mark.Points.Count >= 2:
                {
                    var rect = new Rect(mark.Points[0], mark.Points[1]);
                    dc.DrawRoundedRectangle(null, pen, rect, 2, 2);
                    break;
                }
                case LiveAnnotationTool.Arrow when mark.Points.Count >= 2:
                {
                    var p0 = mark.Points[0];
                    var p1 = mark.Points[1];
                    double len = (p1 - p0).Length;
                    if (len < 2)
                        break;
                    double angle = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X);
                    double head = Math.Clamp(len * 0.28, 10, 18);
                    double wing = 0.45;
                    var w1 = new Point(p1.X - head * Math.Cos(angle - wing), p1.Y - head * Math.Sin(angle - wing));
                    var w2 = new Point(p1.X - head * Math.Cos(angle + wing), p1.Y - head * Math.Sin(angle + wing));
                    var stemEnd = new Point(p1.X - head * 0.68 * Math.Cos(angle), p1.Y - head * 0.68 * Math.Sin(angle));
                    dc.DrawLine(pen, p0, stemEnd);
                    var headGeo = new StreamGeometry();
                    using (var ctx = headGeo.Open())
                    {
                        ctx.BeginFigure(p1, true, true);
                        ctx.LineTo(w1, true, true);
                        ctx.LineTo(w2, true, true);
                    }
                    headGeo.Freeze();
                    dc.DrawGeometry(brush, null, headGeo);
                    break;
                }
                case LiveAnnotationTool.Number when mark.Points.Count >= 1:
                {
                    var c = mark.Points[0];
                    var whiteBorder = new Pen(Brushes.White, 1.8);
                    whiteBorder.Freeze();
                    dc.DrawEllipse(brush, whiteBorder, c, 12, 12);
                    var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                    var label = new FormattedText(mark.Number.ToString(), System.Globalization.CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, typeface, 13, Brushes.White, dpi);
                    dc.DrawText(label, new Point(c.X - label.Width / 2, c.Y - label.Height / 2));
                    break;
                }
            }
        }
    }
}
