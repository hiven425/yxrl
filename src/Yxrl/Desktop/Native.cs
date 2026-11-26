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
    public PixelPoint Center => new(Left + Width / 2,Top + Height / 2);
    public bool Contains(PixelPoint p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
    public PixelRect Intersect(PixelRect other) => new(Math.Max(Left,other.Left), Math.Max(Top,other.Top),
        Math.Min(Right,other.Right), Math.Min(Bottom,other.Bottom));
}
[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo { public int Size; public PixelRect Monitor, Work; public uint Flags; }
internal static class Native
{
    internal delegate bool EnumProc(nint hwnd, nint param);
    internal delegate nint MouseProc(int code, nint message, nint data);
    internal static readonly nint Topmost = -1;
    internal const uint NoActivate = 0x10, NoZOrder = 4, Show = 0x40;
    [DllImport("user32")] internal static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32", CharSet=CharSet.Unicode)] internal static extern nint FindWindow(string name, string? title);
    [DllImport("user32")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] internal static extern bool GetWindowRect(nint hwnd, out PixelRect rect);
    [DllImport("user32", CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder output, int size);
    [DllImport("user32")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32")] internal static extern nint WindowFromPoint(PixelPoint pt);
    [DllImport("user32")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32", CharSet=CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32", EntryPoint="GetWindowLongPtrW")] internal static extern nint GetStyle(nint hwnd, int index);
    [DllImport("user32", EntryPoint="SetWindowLongPtrW")] internal static extern nint SetStyle(nint hwnd, int index, nint value);
    [DllImport("user32", SetLastError=true, CharSet=CharSet.Unicode)] internal static extern nint SetWindowsHookEx(int id, MouseProc proc, nint module, uint thread);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] internal static extern nint GetModuleHandle(string? module);
    [DllImport("user32")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] internal static extern nint CallNextHookEx(nint hook, int code, nint msg, nint data);
    [DllImport("user32")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32")] internal static extern bool AllowSetForegroundWindow(int process);
    [DllImport("user32")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32")] internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32")] internal static extern uint GetPixel(nint dc, int x, int y);
    internal static string ClassOf(nint hwnd) { var text = new StringBuilder(160); GetClassName(hwnd,text,text.Capacity); return text.ToString(); }
    internal static MonitorInfo MonitorOf(nint hwnd)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info);
        return info;
    }
    internal static bool OnTaskbar(nint hwnd, PixelPoint point) => GetAncestor(WindowFromPoint(point),2) == hwnd;
}
