using System.Runtime.InteropServices;
using System.Text;

namespace Yxrl.Desktop;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PixelPoint(int X, int Y);
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool HasArea => Width > 0 && Height > 0;
    public PixelPoint Center => new(Left + Width / 2,Top + Height / 2);
    public bool Contains(PixelPoint p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
    public PixelRect Offset(int x, int y) => new(Left+x,Top+y,Right+x,Bottom+y);
    public PixelRect Intersect(PixelRect other) => new(Math.Max(Left,other.Left), Math.Max(Top,other.Top),
        Math.Min(Right,other.Right), Math.Min(Bottom,other.Bottom));
}
[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo { public int Size; public PixelRect Monitor, Work; public uint Flags; }
[StructLayout(LayoutKind.Sequential)]
internal struct Margins { public int Left, Right, Top, Bottom; }
internal static class Native
{
    internal enum BackdropKind { None = 0, Mica = 2, Acrylic = 3, Tabbed = 4 }
    internal delegate bool EnumProc(nint hwnd, nint param);
    internal delegate nint MouseProc(int code, nint message, nint data);
    internal static readonly nint Topmost = -1, NoTopmost = -2;
    internal const nint WsExLayered = 0x80000, WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    internal const uint NoSize = 1, NoMove = 2, NoActivate = 0x10, NoZOrder = 4, Show = 0x40, NoOwnerZOrder = 0x200;
    private const int DwmwaExtendedFrameBounds = 9, DwmwaUseImmersiveDarkMode = 20, DwmwaWindowCornerPreference = 33,
        DwmwaBorderColor = 34, DwmwaSystemBackdropType = 38, DwmwcpRound = 2;
    [DllImport("user32")] internal static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32", CharSet=CharSet.Unicode)] internal static extern nint FindWindow(string name, string? title);
    [DllImport("user32")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] internal static extern bool GetCursorPos(out PixelPoint point);
    [DllImport("user32")] internal static extern bool GetWindowRect(nint hwnd, out PixelRect rect);
    [DllImport("user32", CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder output, int size);
    [DllImport("user32")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32")] internal static extern nint WindowFromPoint(PixelPoint pt);
    [DllImport("user32")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32", CharSet=CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32", EntryPoint="GetWindowLongPtrW")] internal static extern nint GetStyle(nint hwnd, int index);
    [DllImport("user32", EntryPoint="SetWindowLongPtrW")] internal static extern nint SetStyle(nint hwnd, int index, nint value);
    [DllImport("user32", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);
    internal static void SetPinOverlayStyle(nint hwnd, double opacity, bool clickThrough)
    {
        if (hwnd == 0) return;
        const int gwlExStyle = -20;
        const uint lwaAlpha = 2;
        bool layered = opacity < 0.999 || clickThrough;
        var ex = GetStyle(hwnd, gwlExStyle);
        ex = layered ? (ex | WsExLayered) : (ex & ~WsExLayered);
        ex = clickThrough ? (ex | WsExTransparent) : (ex & ~WsExTransparent);
        SetStyle(hwnd, gwlExStyle, ex);
        if (layered)
            SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Clamp(Math.Round(opacity * 255), 25, 255), lwaAlpha);
    }
    [DllImport("user32")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    internal static bool ExcludeFromCapture(nint hwnd) => hwnd != 0 && SetWindowDisplayAffinity(hwnd, 0x11);
    [DllImport("user32", SetLastError=true, CharSet=CharSet.Unicode)] internal static extern nint SetWindowsHookEx(int id, MouseProc proc, nint module, uint thread);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] internal static extern nint GetModuleHandle(string? module);
    [DllImport("user32")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] internal static extern nint CallNextHookEx(nint hook, int code, nint msg, nint data);
    [DllImport("user32")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32")] internal static extern bool AllowSetForegroundWindow(int process);
    [DllImport("user32")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32")] internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32")] internal static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("gdi32")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32")] private static extern bool DeleteObject(nint handle);
    [DllImport("dwmapi")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out PixelRect value, int size);
    [DllImport("dwmapi")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    internal static string ClassOf(nint hwnd) { var text = new StringBuilder(160); GetClassName(hwnd,text,text.Capacity); return text.ToString(); }
    internal static PixelRect VisibleRect(nint hwnd)
    {
        if (hwnd != 0 && DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<PixelRect>()) == 0 && rect.HasArea)
            return rect;
        return hwnd != 0 && GetWindowRect(hwnd, out var fallback) ? fallback : default;
    }
    internal static bool ApplyDwmStyle(nint hwnd, bool dark, BackdropKind backdrop, uint? borderBgr = null)
    {
        if (hwnd == 0) return false;
        int darkMode = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        int corner = DwmwcpRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        if (borderBgr is { } bgr) {
            int color = unchecked((int)bgr);
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref color, sizeof(int));
        }
        if (backdrop == BackdropKind.None) return false;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(hwnd, ref margins) != 0) return false;
        int kind = (int)backdrop;
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref kind, sizeof(int)) == 0;
    }
    internal static MonitorInfo MonitorOf(nint hwnd)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info);
        return info;
    }
    internal static bool OnTaskbar(nint hwnd, PixelPoint point) => GetAncestor(WindowFromPoint(point),2) == hwnd;
    internal static nint WindowAbove(nint hwnd) => GetWindow(hwnd,3);
    internal static bool IsTopmost(nint hwnd) => (GetStyle(hwnd,-20) & 0x8) != 0;

    internal static bool ClipWindow(nint hwnd, PixelRect? relativeBounds)
    {
        if (relativeBounds is not { } bounds) return SetWindowRgn(hwnd,0,true) != 0;
        var region = CreateRectRgn(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom);
        if (region == 0) return false;
        // The window manager owns the region after a successful call.
        if (SetWindowRgn(hwnd,region,true) != 0) return true;
        DeleteObject(region);
        return false;
    }
}
