using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal enum RecordingFrameState
{
    Ready,
    Countdown,
    Live,
    Paused
}

internal sealed class RecordingRegionFrame : Window
{
    private readonly PixelRect area;
    private readonly FrameVisual visual = new();
    private readonly DispatcherTimer pulseTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private long pulseStartTick;
    private bool closed, positioning;

    internal PixelRect FrameBounds { get; private set; }
    internal bool CaptureExcluded { get; private set; }
    internal RecordingFrameState State => visual.State;

    public RecordingRegionFrame(PixelRect area)
    {
        this.area = area;
        Title = "YXRL 录制范围";
        AutomationProperties.SetAutomationId(this, "YxrlRecordingFrame");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Focusable = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        Content = visual;
        pulseTimer.Tick += OnPulseTick;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            const int gwlExStyle = -20;
            Native.SetStyle(hwnd, gwlExStyle,
                Native.GetStyle(hwnd, gwlExStyle) | Native.WsExTransparent | Native.WsExToolWindow | Native.WsExNoActivate);
            CaptureExcluded = Native.ExcludeFromCapture(hwnd);
            PositionFrame(hwnd);
        };
        Loaded += (_, _) => PositionFrame(new WindowInteropHelper(this).Handle);
        DpiChanged += (_, _) => PositionFrame(new WindowInteropHelper(this).Handle);
        Closed += (_, _) =>
        {
            closed = true;
            pulseTimer.Stop();
        };
    }

    internal static PixelRect CalculateBounds(PixelRect area, PixelRect bounds, int outset)
    {
        int left = area.Left - outset, top = area.Top - outset, right = area.Right + outset, bottom = area.Bottom + outset;
        if (bounds.HasArea)
        {
            left = Math.Max(bounds.Left, left);
            top = Math.Max(bounds.Top, top);
            right = Math.Min(bounds.Right, right);
            bottom = Math.Min(bounds.Bottom, bottom);
            if (right <= left || bottom <= top)
                return area;
        }
        return new(left, top, right, bottom);
    }

    private void PositionFrame(nint hwnd)
    {
        if (hwnd == 0 || closed || positioning)
            return;
        positioning = true;
        try
        {
            // Move near target region first so GetDpiForWindow reads the target monitor's DPI scale.
            Native.SetWindowPos(hwnd, Native.Topmost, area.Left, area.Top, 1, 1, Native.NoActivate | Native.NoOwnerZOrder);
            double dpiScale = Math.Max(1, Native.GetDpiForWindow(hwnd) / 96.0);
            int outset = Math.Clamp((int)Math.Round(4 * dpiScale), 3, 6);
            var rect = CalculateBounds(area, DesktopFrame.VirtualBounds(), outset);
            FrameBounds = rect;
            Width = rect.Width / dpiScale;
            Height = rect.Height / dpiScale;
            Native.SetWindowPos(hwnd, Native.Topmost, rect.Left, rect.Top, rect.Width, rect.Height,
                Native.NoActivate | Native.NoOwnerZOrder);
        }
        finally
        {
            positioning = false;
        }
    }

    public void UpdateState(RecordingFrameState nextState, int countdownSeconds = 0)
    {
        if (closed)
            return;
        visual.SetState(nextState, countdownSeconds);
        if (nextState == RecordingFrameState.Live)
        {
            if (!pulseTimer.IsEnabled)
            {
                pulseStartTick = Environment.TickCount64;
                pulseTimer.Start();
            }
        }
        else
        {
            pulseTimer.Stop();
            Opacity = 1.0;
        }
    }

    public void CloseSafely()
    {
        if (closed)
            return;
        closed = true;
        pulseTimer.Stop();
        Close();
    }

    private void OnPulseTick(object? sender, EventArgs e)
    {
        if (closed || visual.State != RecordingFrameState.Live)
        {
            pulseTimer.Stop();
            Opacity = 1.0;
            return;
        }
        double elapsedSeconds = (Environment.TickCount64 - pulseStartTick) / 1000.0;
        Opacity = 0.86 + 0.14 * Math.Cos(elapsedSeconds * Math.PI * 1.4);
    }

    private sealed class FrameVisual : FrameworkElement
    {
        private static readonly Brush BackdropShadow = new SolidColorBrush(Color.FromArgb(145, 14, 20, 28));
        private static readonly Brush BadgeFill = new SolidColorBrush(Color.FromArgb(204, 18, 25, 34));
        private static readonly Pen BadgeBorder = new(new SolidColorBrush(Color.FromArgb(190, 249, 115, 22)), 1.5);
        private static readonly Color ReadyColor = Color.FromRgb(24, 147, 238);
        private static readonly Color CountdownColor = Color.FromRgb(249, 115, 22);
        private static readonly Color LiveColor = Color.FromRgb(224, 49, 49);
        private static readonly Color PausedColor = Color.FromRgb(217, 119, 6);
        private static readonly Typeface BadgeTypeface = new(new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        static FrameVisual()
        {
            BackdropShadow.Freeze();
            BadgeFill.Freeze();
            BadgeBorder.Freeze();
        }

        private int countdownSeconds;
        public RecordingFrameState State { get; private set; } = RecordingFrameState.Ready;

        public FrameVisual() => IsHitTestVisible = false;

        public void SetState(RecordingFrameState nextState, int seconds)
        {
            if (State == nextState && countdownSeconds == seconds)
                return;
            State = nextState;
            countdownSeconds = seconds;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = RenderSize.Width, h = RenderSize.Height;
            if (w < 4 || h < 4)
                return;

            var accent = State switch
            {
                RecordingFrameState.Countdown => CountdownColor,
                RecordingFrameState.Live => LiveColor,
                RecordingFrameState.Paused => PausedColor,
                _ => ReadyColor
            };
            bool dashed = State is RecordingFrameState.Ready or RecordingFrameState.Paused;
            double stroke = State == RecordingFrameState.Ready ? 2.0 : 2.5;
            double cornerStroke = State == RecordingFrameState.Live ? 3.2 : 3.0;
            double inset = stroke / 2.0 + 0.5;
            var rect = new Rect(inset, inset, Math.Max(1, w - inset * 2), Math.Max(1, h - inset * 2));

            var shadowPen = new Pen(BackdropShadow, stroke + 1.4);
            shadowPen.Freeze();
            dc.DrawRectangle(null, shadowPen, rect);

            var mainBrush = new SolidColorBrush(accent);
            mainBrush.Freeze();
            var borderPen = new Pen(mainBrush, stroke)
            {
                DashStyle = dashed ? new DashStyle([6, 4], 0) : DashStyles.Solid
            };
            borderPen.Freeze();
            dc.DrawRectangle(null, borderPen, rect);

            double arm = Math.Clamp(Math.Min(w, h) * 0.25, 6, 16);
            double cInset = cornerStroke / 2.0;
            var cShadowPen = new Pen(BackdropShadow, cornerStroke + 1.2)
            {
                StartLineCap = PenLineCap.Square,
                EndLineCap = PenLineCap.Square
            };
            var cPen = new Pen(mainBrush, cornerStroke)
            {
                StartLineCap = PenLineCap.Square,
                EndLineCap = PenLineCap.Square
            };
            cShadowPen.Freeze();
            cPen.Freeze();

            void DrawCorner(double cx, double cy, int sx, int sy)
            {
                var p1 = new Point(cx + sx * arm, cy);
                var p2 = new Point(cx, cy);
                var p3 = new Point(cx, cy + sy * arm);
                dc.DrawLine(cShadowPen, p1, p2);
                dc.DrawLine(cShadowPen, p2, p3);
                dc.DrawLine(cPen, p1, p2);
                dc.DrawLine(cPen, p2, p3);
            }

            DrawCorner(cInset, cInset, 1, 1);
            DrawCorner(w - cInset, cInset, -1, 1);
            DrawCorner(cInset, h - cInset, 1, -1);
            DrawCorner(w - cInset, h - cInset, -1, -1);

            if (State == RecordingFrameState.Countdown && countdownSeconds > 0 && w >= 72 && h >= 56)
            {
                double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                var formatted = new FormattedText(countdownSeconds.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, BadgeTypeface, 26, Brushes.White, pixelsPerDip);
                double bw = Math.Max(46, formatted.Width + 24);
                double bh = Math.Max(42, formatted.Height + 10);
                var pill = new Rect((w - bw) / 2, (h - bh) / 2, bw, bh);
                dc.DrawRoundedRectangle(BadgeFill, BadgeBorder, pill, 12, 12);
                dc.DrawText(formatted, new Point((w - formatted.Width) / 2, (h - formatted.Height) / 2));
            }
        }
    }
}
