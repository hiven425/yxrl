using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Yxrl.Desktop;

internal sealed record ClockAnchor(nint Taskbar, PixelRect Bounds, PixelRect Bar, PixelRect Work, uint Dpi);
internal sealed record Discovery(ClockAnchor[] Clocks, bool OtherClock, bool Retracted);
internal static class ClockLocator
{
    public static Discovery Find()
    {
        var bars = new List<nint>();
        Native.EnumWindows((hwnd, _) => {
            if (Native.IsWindowVisible(hwnd) && Native.ClassOf(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") bars.Add(hwnd);
            return true;
        },0);
        var clocks = new List<ClockAnchor>(); bool other = false, retracted = false;
        foreach (var hwnd in bars)
        {
            if (!Native.GetWindowRect(hwnd,out var bar)) continue;
            var monitor = Native.MonitorOf(hwnd);
            var visible = bar.Intersect(monitor.Monitor);
            if (visible.Height < 10 || visible.Width < 10) { retracted = true; continue; }
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
                if (rect.IsEmpty) continue;
                var dpi = Native.GetDpiForWindow(hwnd);
                var bounds = new PixelRect((int)Math.Floor(rect.Left),(int)Math.Floor(rect.Top),
                    (int)Math.Ceiling(rect.Right),(int)Math.Ceiling(rect.Bottom)).Intersect(bar).Intersect(monitor.Monitor);
                var scale = dpi / 96.0;
                if (bounds.Width < 24 * scale || bounds.Height < 14 * scale || bounds.Width > 320 * scale) continue;
                clocks.Add(new(hwnd,bounds,bar,monitor.Work,dpi));
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException or ArgumentException)
            { /* Explorer can rebuild its tree mid-enumeration. The next scan starts at the new HWND. */ }
        }
        return new(clocks.ToArray(),other,retracted);
    }
    private static PropertyCondition Property(AutomationProperty key, string value) => new(key,value);
}
