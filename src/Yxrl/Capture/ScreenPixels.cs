using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yxrl.Desktop;

namespace Yxrl.Capture;

// A capture and all of its GDI handles stay on the thread that acquired the screen DC.
internal sealed class ScreenPixels : IDisposable
{
    private readonly PixelRect bounds;
    private nint screen, memory, bitmap, previous, pixels;
    public int Stride { get; }
    public int Length { get; }

    public ScreenPixels(PixelRect bounds)
    {
        this.bounds = bounds;
        Stride = checked(bounds.Width * 4);
        Length = checked(Stride * bounds.Height);
        try
        {
            var header = new BitmapHeader
            {
                Size = Marshal.SizeOf<BitmapHeader>(),
                Width = bounds.Width,
                Height = -bounds.Height,
                Planes = 1,
                Bits = 32
            };
            screen = Native.GetDC(0);
            if (screen == 0 || (memory = CreateCompatibleDC(screen)) == 0 ||
                (bitmap = CreateDIBSection(screen, ref header, 0, out pixels, 0, 0)) == 0 || pixels == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法分配桌面图像。");
            previous = SelectObject(memory, bitmap);
            if (previous == 0 || previous == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法选中捕获用位图。");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void Capture(bool includeCursor)
    {
        if (!BitBlt(memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.Left, bounds.Top, 0x40CC0020))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未能读取桌面像素。");
        if (includeCursor)
            DrawCursor();
        GdiFlush();
    }

    public BitmapSource Image()
    {
        Capture(false);
        var image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null, pixels, Length, Stride);
        image.Freeze();
        return image;
    }

    public void CopyFrame(byte[] destination, bool includeCursor)
    {
        Capture(includeCursor);
        Marshal.Copy(pixels, destination, 0, Length);
    }

    private void DrawCursor()
    {
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref cursor) || cursor.Flags != 1 || cursor.Cursor == 0 ||
            !GetIconInfo(cursor.Cursor, out var icon))
            return;
        try
        {
            DrawIconEx(memory, cursor.Position.X - bounds.Left - icon.HotX, cursor.Position.Y - bounds.Top - icon.HotY,
                cursor.Cursor, 0, 0, 0, 0, 3);
        }
        finally
        {
            if (icon.Mask != 0) DeleteObject(icon.Mask);
            if (icon.Color != 0) DeleteObject(icon.Color);
        }
    }

    public void Dispose()
    {
        if (previous != 0 && previous != -1) SelectObject(memory, previous);
        if (bitmap != 0) DeleteObject(bitmap);
        if (memory != 0) DeleteDC(memory);
        if (screen != 0) Native.ReleaseDC(0, screen);
        previous = bitmap = memory = screen = pixels = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Size, Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint Used, Important;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size, Flags;
        public nint Cursor;
        public PixelPoint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon, HotX, HotY;
        public nint Mask, Color;
    }

    [DllImport("gdi32", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapHeader header, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32")] private static extern bool GdiFlush();
    [DllImport("gdi32", SetLastError = true)] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32", SetLastError = true)] private static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32")] private static extern bool DeleteObject(nint value);
    [DllImport("user32")] private static extern bool GetCursorInfo(ref CursorInfo cursor);
    [DllImport("user32")] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32")] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
}
