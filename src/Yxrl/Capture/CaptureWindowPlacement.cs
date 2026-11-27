using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal static class CaptureWindowPlacement
{
    public static bool Place(Window window, PixelRect area)
    {
        const uint ExcludeFromCapture = 0x11;
        nint hwnd = new WindowInteropHelper(window).Handle;
        var work = DesktopFrame.WorkAreaAt(area.Center);
        if (!work.HasArea) work = Native.MonitorOf(hwnd).Work;
        if (!work.HasArea) return false;
        double scale = Math.Max(1, Native.GetDpiForWindow(hwnd) / 96.0);
        int width = (int)Math.Ceiling(window.ActualWidth * scale), height = (int)Math.Ceiling(window.ActualHeight * scale);
        int Clamp(int value, int low, int high) => Math.Clamp(value, low, Math.Max(low, high));
        bool exclude = SetWindowDisplayAffinity(hwnd, ExcludeFromCapture);
        var positions = new[]
        {
            new PixelPoint(area.Left, area.Bottom + 10), new PixelPoint(area.Left, area.Top - height - 10),
            new PixelPoint(area.Right + 10, area.Top), new PixelPoint(area.Left - width - 10, area.Top)
        };
        foreach (var position in positions)
        {
            int x = Clamp(position.X, work.Left, work.Right - width), y = Clamp(position.Y, work.Top, work.Bottom - height);
            if (new PixelRect(x, y, x + width, y + height).Intersect(area).HasArea)
                continue;
            return Native.SetWindowPos(hwnd, Native.Topmost, x, y, 0, 0, Native.NoSize | Native.NoActivate);
        }
        return Native.SetWindowPos(hwnd, Native.Topmost, Math.Max(work.Left, work.Right - width), Math.Max(work.Top, work.Bottom - height),
            0, 0, Native.NoSize | Native.NoActivate) && exclude;
    }

    [DllImport("user32")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
