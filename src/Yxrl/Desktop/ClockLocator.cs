using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Yxrl.Desktop;

internal sealed record ClockAnchor(nint Taskbar, PixelRect Bounds, PixelRect Bar, PixelRect Work, uint Dpi, PixelRect Monitor)
{
    // Auto-hide translates the entire taskbar along its short axis. Resizing or
    // moving it to a different edge requires a new measurement from Explorer.
    public bool CanFollow(PixelRect bar)
    {
        if (bar.Width != Bar.Width || bar.Height != Bar.Height) return false;
        return Bar.Width >= Bar.Height
            ? bar.Left == Bar.Left && Math.Abs(bar.Top-Bar.Top) <= Bar.Height
            : bar.Top == Bar.Top && Math.Abs(bar.Left-Bar.Left) <= Bar.Width;
    }

    public ClockPlacement? PlaceAt(PixelRect bar, uint dpi)
    {
        if (dpi != Dpi || !CanFollow(bar) || ClockLocator.IsRetracted(bar,Monitor)) return null;
        var bounds = Bounds.Offset(bar.Left-Bar.Left,bar.Top-Bar.Top);
        var visible = bounds.Intersect(Monitor);
        return visible.HasArea ? new(bounds,visible) : null;
    }
}
internal readonly record struct ClockPlacement(PixelRect Bounds, PixelRect Visible)
{
    public PixelRect? Clip => Bounds == Visible ? null : Visible.Offset(-Bounds.Left,-Bounds.Top);
}
internal sealed record Discovery(ClockAnchor[] Clocks, nint[] Deferred, bool OtherClock, bool Retracted)
{
    public ClockAnchor[] MergeWith(ClockAnchor[] previous) =>
        Clocks.Concat(previous.Where(anchor => Deferred.Contains(anchor.Taskbar))).ToArray();
}
internal static class ClockLocator
{
    internal static bool IsRetracted(PixelRect bar, PixelRect monitor)
    {
        var visible = bar.Intersect(monitor);
        return visible.Height < 10 || visible.Width < 10;
    }

    public static Discovery Find(IReadOnlyList<ClockAnchor>? previous = null)
    {
        var bars = new List<nint>();
        Native.EnumWindows((hwnd, _) => {
            if (Native.ClassOf(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") bars.Add(hwnd);
            return true;
        },0);
        var clocks = new List<ClockAnchor>();
        var deferred = new List<nint>();
        bool other = false, retracted = false;
        foreach (var hwnd in bars)
        {
            if (!Native.GetWindowRect(hwnd,out var bar)) continue;
            var monitor = Native.MonitorOf(hwnd);
            var earlier = previous?.FirstOrDefault(anchor => anchor.Taskbar == hwnd && anchor.CanFollow(bar));
            var screen = earlier?.Monitor ?? monitor.Monitor;
            var visible = bar.Intersect(screen);
            bool sliding = bar.Width >= bar.Height ? visible.Height < bar.Height-2 : visible.Width < bar.Width-2;
            if (!Native.IsWindowVisible(hwnd) || IsRetracted(bar,screen) || sliding) {
                retracted = true;
                deferred.Add(hwnd);
                continue;
            }
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                // Find local, language-independent UIA identifiers, including Windows 11 XAML raw children.
                var condition = new OrCondition(
                    Property(AutomationElement.ClassNameProperty,"YXCalendar.Win11.TouchClock"),
                    Property(AutomationElement.ClassNameProperty,"TrayClockWClass"),
                    Property(AutomationElement.AutomationIdProperty,"SystemTray.DateTimeIcon"),
                    Property(AutomationElement.ClassNameProperty,"SystemTray.DateTimeIcon"),
                    Property(AutomationElement.ClassNameProperty,"SystemTray.OmniButton"),
                    Property(AutomationElement.AutomationIdProperty,"DateTimeButton"),
                    Property(AutomationElement.AutomationIdProperty,"TimeInnerTextBlock"),
                    Property(AutomationElement.AutomationIdProperty,"DateInnerTextBlock"));
                var nodes = root.FindAll(TreeScope.Descendants,condition).Cast<AutomationElement>().ToList();
                if (nodes.Any(n => n.Current.ClassName == "YXCalendar.Win11.TouchClock" && !n.Current.BoundingRectangle.IsEmpty))
                { other = true; continue; }
                var button = nodes.FirstOrDefault(n => n.Current.ClassName is "TrayClockWClass" or "SystemTray.DateTimeIcon" ||
                    n.Current.AutomationId is "SystemTray.DateTimeIcon" or "DateTimeButton");
                var rect = button?.Current.BoundingRectangle ?? System.Windows.Rect.Empty;
                if (rect.IsEmpty) {
                    // 25H2 exposes the same OmniButton class on multiple system controls.
                    // Its time/date text exists only in the raw tree and identifies the clock without locale matching.
                    foreach (var candidate in nodes.Where(n => n.Current.ClassName == "SystemTray.OmniButton")) {
                        var walker = TreeWalker.RawViewWalker;
                        var textBounds = System.Windows.Rect.Empty;
                        bool hasTime = false, hasDate = false, ownsOther = false;
                        for (var child = walker.GetFirstChild(candidate); child is not null; child = walker.GetNextSibling(child)) {
                            var item = child.Current;
                            if (item.BoundingRectangle.IsEmpty || item.IsOffscreen) continue;
                            if (item.AutomationId is "TimeInnerTextBlock" or "DateInnerTextBlock") {
                                hasTime |= item.AutomationId == "TimeInnerTextBlock";
                                hasDate |= item.AutomationId == "DateInnerTextBlock";
                                textBounds.Union(item.BoundingRectangle);
                            } else ownsOther = true;
                        }
                        if (!hasTime && !hasDate) continue;
                        rect = ownsOther ? textBounds : candidate.Current.BoundingRectangle;
                        break;
                    }
                }
                if (rect.IsEmpty) {
                    foreach (var n in nodes.Where(n => n.Current.AutomationId is "TimeInnerTextBlock" or "DateInnerTextBlock"))
                        rect.Union(n.Current.BoundingRectangle);
                }
                if (!Native.GetWindowRect(hwnd,out var measuredBar) || measuredBar != bar) {
                    // The native clock can move while the accessibility tree is being read.
                    deferred.Add(hwnd);
                    continue;
                }
                if (rect.IsEmpty) continue;
                var dpi = Native.GetDpiForWindow(hwnd);
                var bounds = new PixelRect((int)Math.Floor(rect.Left),(int)Math.Floor(rect.Top),
                    (int)Math.Ceiling(rect.Right),(int)Math.Ceiling(rect.Bottom)).Intersect(bar);
                // During a first reveal wait for the complete clock rectangle before
                // recording its offset, even if UI Automation already returns a button.
                if (bounds.Intersect(monitor.Monitor) != bounds) {
                    retracted = true;
                    deferred.Add(hwnd);
                    continue;
                }
                var scale = dpi / 96.0;
                if (bounds.Width < 24 * scale || bounds.Height < 14 * scale || bounds.Width > 320 * scale) continue;
                clocks.Add(new(hwnd,bounds,bar,monitor.Work,dpi,monitor.Monitor));
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException or ArgumentException)
            { deferred.Add(hwnd); }
        }
        return new(clocks.ToArray(),deferred.ToArray(),other,retracted);
    }
    private static PropertyCondition Property(AutomationProperty key, string value) => new(key,value);
}
