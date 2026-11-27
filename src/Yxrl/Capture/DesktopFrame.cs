using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed record DesktopFrame(BitmapSource Image, PixelRect Bounds, PixelRect[] Windows, PixelRect[]? Monitors = null, nint[]? WindowHandles = null)
{
    [DllImport("user32")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32")]
    private static extern bool IsIconic(nint hwnd);
    [DllImport("user32")]
    private static extern nint MonitorFromPoint(PixelPoint point, uint mode);
    [DllImport("user32")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    private delegate bool MonitorCallback(nint monitor, nint dc, ref PixelRect rect, nint data);
    [DllImport("user32")]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("dwmapi")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int bytes);
    [DllImport("wtsapi32", EntryPoint = "WTSQuerySessionInformationW")]
    private static extern bool QuerySession(nint server, int session, int kind, out nint buffer, out int bytes);
    [DllImport("wtsapi32")]
    private static extern void WTSFreeMemory(nint buffer);
    internal static bool SessionLocked()
    {
        const int currentSession = -1, wtsSessionInfoEx = 25;
        if (!QuerySession(0, currentSession, wtsSessionInfoEx, out var buffer, out int bytes))
            return false;
        try
        {
            // Win-x64 WTSINFOEXW aligns its level-1 union at byte 8. Read only the level and lock flag.
            return bytes >= 20 && Marshal.ReadInt32(buffer) == 1 && Marshal.ReadInt32(buffer, 16) == 0;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    public static PixelRect WorkAreaAt(PixelPoint point)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return Native.GetMonitorInfo(MonitorFromPoint(point, 2), ref info) ? info.Work : default;
    }

    internal static PixelRect VirtualBounds()
    {
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79);
        return w > 0 && h > 0 ? new PixelRect(x, y, x + w, y + h) : default;
    }

    internal static bool HasActiveDisplay() => GetDisplayConfigBufferSizes(2, out uint paths, out _) == 0 && paths > 0;

    public static DesktopFrame Freeze()
    {
        if (SessionLocked())
            throw new InvalidOperationException("Windows 桌面已锁定，请解锁后重新截图。");
        var bounds = VirtualBounds();
        if (!bounds.HasArea)
            throw new InvalidOperationException("Windows 没有可捕获的桌面。");
        var monitors = new List<PixelRect>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref PixelRect rect, nint data) =>
        {
            monitors.Add(rect);
            return true;
        }, 0);
        var windows = new List<PixelRect>();
        var handles = new List<nint>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || IsIconic(hwnd) || (DwmGetWindowAttribute(hwnd, 14, out int cloaked, 4) == 0 && cloaked != 0))
                return true;
            var rect = Native.VisibleRect(hwnd).Intersect(bounds);
            if (rect.HasArea)
            {
                windows.Add(rect);
                handles.Add(hwnd);
            }
            return true;
        }, 0);
        using var pixels = new ScreenPixels(bounds);
        return new(pixels.Image(), bounds, windows.ToArray(), monitors.ToArray(), handles.ToArray());
    }

    internal PixelRect ToolsArea(PixelRect selection)
    {
        var absolute = selection.Offset(Bounds.Left, Bounds.Top);
        var monitor = Monitors is { Length: > 0 } ? Monitors.MaxBy(m =>
        {
            var overlap = m.Intersect(absolute);
            return overlap.HasArea ? (long)overlap.Width * overlap.Height : 0;
        }) : Bounds;
        return monitor.Offset(-Bounds.Left, -Bounds.Top);
    }

    public PixelRect MonitorAt(PixelPoint local)
    {
        var absolute = new PixelPoint(local.X + Bounds.Left, local.Y + Bounds.Top);
        var monitor = Monitors?.FirstOrDefault(m => m.Contains(absolute)) ?? default;
        return (monitor.HasArea ? monitor : Bounds).Offset(-Bounds.Left, -Bounds.Top);
    }

    public PixelRect WindowAt(PixelPoint local)
    {
        var absolute = new PixelPoint(local.X + Bounds.Left, local.Y + Bounds.Top);
        var hit = Windows.FirstOrDefault(r => r.Contains(absolute));
        return hit.HasArea ? hit.Offset(-Bounds.Left, -Bounds.Top) : MonitorAt(local);
    }

    // The screenshot overlay covers the pointer. Start the search at the original window.
    internal PixelRect ControlAt(PixelPoint local)
    {
        if (WindowHandles is null)
            return default;
        var absolute = new PixelPoint(local.X + Bounds.Left, local.Y + Bounds.Top);
        int index = Array.FindIndex(Windows, bounds => bounds.Contains(absolute));
        if (index < 0)
            return default;

        try
        {
            var window = AutomationElement.FromHandle(WindowHandles[index]);
            var walker = TreeWalker.ControlViewWalker;
            var cache = new CacheRequest();
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.ControlTypeProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            var point = new Point(absolute.X, absolute.Y);
            int budget = 350;
            PixelRect best = default;
            void Search(AutomationElement parent, int level)
            {
                if (level >= 24 || budget <= 0)
                    return;
                for (var child = walker.GetFirstChild(parent, cache); child is not null && budget-- > 0; child = walker.GetNextSibling(child, cache))
                {
                    var bounds = child.Cached.BoundingRectangle;
                    if (child.Cached.IsOffscreen || (!bounds.IsEmpty && !bounds.Contains(point)))
                        continue;
                    if (!bounds.IsEmpty && child.Cached.ControlType is { } type &&
                        type != ControlType.Pane && type != ControlType.Group && type != ControlType.Window &&
                        type != ControlType.Custom && type != ControlType.Document)
                    {
                        var hit = new PixelRect((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
                            (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom)).Intersect(Windows[index]);
                        if (hit.Contains(absolute) && hit != Windows[index] &&
                            (!best.HasArea || (long)hit.Width * hit.Height < (long)best.Width * best.Height))
                            best = hit;
                    }
                    Search(child, level + 1);
                }
            }
            Search(window, 0);
            return best.HasArea ? best.Offset(-Bounds.Left, -Bounds.Top) : default;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException or ArgumentException)
        {
            return default;
        }
    }
}
