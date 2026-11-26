using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed record DesktopFrame(BitmapSource Image, PixelRect Bounds, PixelRect[] Windows, PixelRect[]? Monitors = null)
{
    [DllImport("user32")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32")]
    private static extern bool IsIconic(nint hwnd);
    private delegate bool MonitorCallback(nint monitor, nint dc, ref PixelRect rect, nint data);
    [DllImport("user32")]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("dwmapi")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int bytes);
    [DllImport("gdi32", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint dc);
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Size, Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint Used, Important;
    }

    [DllImport("gdi32", SetLastError = true)]
    private static extern nint CreateDIBSection(nint dc, ref BitmapHeader info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32")]
    private static extern bool GdiFlush();
    [DllImport("gdi32")]
    private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32", SetLastError = true)]
    private static extern bool BitBlt(nint target, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32")]
    private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32")]
    private static extern bool DeleteObject(nint obj);
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

    public static DesktopFrame Freeze()
    {
        if (SessionLocked())
            throw new InvalidOperationException("Windows 桌面已锁定，请解锁后重新截图。");
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79);
        var bounds = new PixelRect(x, y, x + w, y + h);
        if (!bounds.HasArea)
            throw new InvalidOperationException("Windows 没有可捕获的桌面。");
        var monitors = new List<PixelRect>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref PixelRect rect, nint data) =>
        {
            monitors.Add(rect);
            return true;
        }, 0);
        var windows = new List<PixelRect>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || IsIconic(hwnd) || (DwmGetWindowAttribute(hwnd, 14, out int cloaked, 4) == 0 && cloaked != 0))
                return true;
            var rect = Native.VisibleRect(hwnd).Intersect(bounds);
            if (rect.HasArea)
                windows.Add(rect);
            return true;
        }, 0);
        nint screen = Native.GetDC(0), memory = 0, bitmap = 0, previous = 0;
        try
        {
            var header = new BitmapHeader
            {
                Size = Marshal.SizeOf<BitmapHeader>(),
                Width = w,
                Height = -h,
                Planes = 1,
                Bits = 32
            };
            nint pixels = 0;
            if (screen == 0 || (memory = CreateCompatibleDC(screen)) == 0 || (bitmap = CreateDIBSection(screen, ref header, 0, out pixels, 0, 0)) == 0 || pixels == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法分配桌面图像。");
            previous = SelectObject(memory, bitmap);
            if (previous == 0 || previous == -1 || !BitBlt(memory, 0, 0, w, h, screen, x, y, 0x40CC0020))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未能读取桌面像素。");
            GdiFlush();
            // A top-down GDI DIB has BGR pixels and an unused fourth byte.
            int stride = checked(w * 4);
            var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, pixels, checked(stride * h), stride);
            image.Freeze();
            return new(image, bounds, windows.ToArray(), monitors.ToArray());
        }
        finally
        {
            if (previous != 0 && previous != -1)
                SelectObject(memory, previous);
            if (bitmap != 0)
                DeleteObject(bitmap);
            if (memory != 0)
                DeleteDC(memory);
            if (screen != 0)
                Native.ReleaseDC(0, screen);
        }
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

    public PixelRect WindowAt(PixelPoint local)
    {
        var absolute = new PixelPoint(local.X + Bounds.Left, local.Y + Bounds.Top);
        var hit = Windows.FirstOrDefault(r => r.Contains(absolute));
        if (!hit.HasArea)
            hit = Monitors?.FirstOrDefault(m => m.Contains(absolute)) ?? default;
        return (hit.HasArea ? hit : Bounds).Offset(-Bounds.Left, -Bounds.Top);
    }
}
