using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using SkiaSharp;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal enum CueKind { Left, Right, Middle, WheelUp, WheelDown, Shortcut }
internal sealed record InputCue(long Time, CueKind Kind, int X, int Y, string? Label = null);

internal sealed class RecordingInput : IDisposable
{
    private readonly PixelRect region;
    private readonly nint controller;
    private readonly Func<long?> time;
    private readonly Action<InputCue> add;
    private readonly Native.MouseProc mouseProc, keyProc;
    private nint mouseHook, keyHook;
    private int lastKey;

    public RecordingInput(PixelRect region, nint controller, bool mouse, bool keys, Func<long?> time, Action<InputCue> add)
    {
        this.region = region;
        this.controller = controller;
        this.time = time;
        this.add = add;
        mouseProc = Mouse;
        keyProc = Keyboard;
        try
        {
            if (mouse) mouseHook = Hook(14, mouseProc);
            if (keys) keyHook = Hook(13, keyProc);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static nint Hook(int id, Native.MouseProc callback)
    {
        var hook = Native.SetWindowsHookEx(id, callback, Native.GetModuleHandle(null), 0);
        if (hook == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows 无法启用录屏输入动画。");
        return hook;
    }

    private nint Mouse(int code, nint message, nint data)
    {
        if (code == 0 && message is 0x201 or 0x204 or 0x207 or 0x20a && time() is { } at)
        {
            var point = Marshal.PtrToStructure<PixelPoint>(data);
            if (region.Contains(point) && Native.GetAncestor(Native.WindowFromPoint(point), 2) != controller)
            {
                var kind = (int)message switch
                {
                    0x201 => CueKind.Left,
                    0x204 => CueKind.Right,
                    0x207 => CueKind.Middle,
                    _ => Marshal.ReadInt16(data, 10) >= 0 ? CueKind.WheelUp : CueKind.WheelDown
                };
                add(new(at, kind, point.X - region.Left, point.Y - region.Top));
            }
        }
        return Native.CallNextHookEx(mouseHook, code, message, data);
    }

    private nint Keyboard(int code, nint message, nint data)
    {
        if (code == 0 && message is 0x100 or 0x104 or 0x101 or 0x105)
        {
            int key = Marshal.ReadInt32(data);
            if (message is 0x101 or 0x105)
            {
                if (lastKey == key) lastKey = 0;
            }
            else if (lastKey != key && time() is { } at)
            {
                lastKey = key;
                var front = GetForegroundWindow();
                if (front != 0 && front != controller && Native.VisibleRect(front).Intersect(region).HasArea &&
                    !(Down(0xa5) && Down(0x11)) && // AltGr can enter normal characters.
                    Label(key, Down(0x11), Down(0x12), Down(0x5b) || Down(0x5c), Down(0x10)) is { } label)
                    add(new(at, CueKind.Shortcut, 0, 0, label));
            }
        }
        return Native.CallNextHookEx(keyHook, code, message, data);
    }

    internal static string? Label(int key, bool ctrl, bool alt, bool win, bool shift)
    {
        string? name = key switch
        {
            >= 0x70 and <= 0x87 => $"F{key - 0x6f}",
            _ when !(ctrl || alt || win) => null,
            >= '0' and <= '9' or >= 'A' and <= 'Z' => ((char)key).ToString(),
            0x09 => "Tab", 0x0d => "Enter", 0x1b => "Esc", 0x20 => "Space",
            0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓", 0x2e => "Delete",
            _ => null
        };
        return name is null ? null : (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (win ? "Win+" : "") +
            (shift ? "Shift+" : "") + name;
    }

    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public void Dispose()
    {
        if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
        if (keyHook != 0) Native.UnhookWindowsHookEx(keyHook);
        mouseHook = keyHook = 0;
    }

    [DllImport("user32")] private static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern short GetAsyncKeyState(int key);
}

internal sealed class CueTimeline(InputCue[] events)
{
    internal const long MouseLength = 6_500_000, KeyLength = 14_000_000;
    public InputCue[] Events { get; } = events;

    public IEnumerable<(InputCue Cue, double Age)> Visible(long at, bool mouse, bool keys)
    {
        if (!mouse && !keys)
            yield break;
        long oldest = at - KeyLength;
        int low = 0, high = Events.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (Events[mid].Time < oldest) low = mid + 1;
            else high = mid;
        }
        int shortcut = -1;
        for (int i = low; i < Events.Length && Events[i].Time <= at; i++)
        {
            var cue = Events[i];
            if (cue.Kind == CueKind.Shortcut)
                shortcut = i;
            else if (mouse && at - cue.Time < MouseLength)
                yield return (cue, (double)(at - cue.Time) / MouseLength);
        }
        if (keys && shortcut >= 0)
            yield return (Events[shortcut], (double)(at - Events[shortcut].Time) / KeyLength);
    }

    public static SKColor Color(CueKind kind, byte alpha) => kind switch
    {
        CueKind.Right => new(248, 183, 61, alpha),
        CueKind.Middle or CueKind.WheelUp or CueKind.WheelDown => new(100, 224, 185, alpha),
        _ => new(101, 199, 255, alpha)
    };

    public static (double Radius, byte Opacity) Ring(double age, double unit) =>
        ((8 + 19 * age) * unit, (byte)(240 * (1 - age)));

    public static string? Wheel(CueKind kind) => kind switch
    {
        CueKind.WheelUp => "↑", CueKind.WheelDown => "↓", _ => null
    };

    public static double Units(int height) => Math.Clamp(height / 420.0, .48, 3);
}

internal sealed class CuePainter : IDisposable
{
    private readonly SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
    private readonly SKPaint paint = new() { IsAntialias = true };
    private readonly SKFont font;
    private readonly CueTimeline timeline;

    public CuePainter(CueTimeline timeline)
    {
        this.timeline = timeline;
        font = new SKFont(typeface);
    }

    public void Draw(byte[] data, int width, int height, long time, bool mouse, bool keys)
    {
        var shown = timeline.Visible(time, mouse, keys).ToArray();
        if (shown.Length == 0) return;
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            using var surface = SKSurface.Create(new(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque),
                pin.AddrOfPinnedObject(), width * 4);
            var canvas = surface.Canvas;
            float unit = (float)CueTimeline.Units(height);
            font.Size = 18 * unit;
            foreach (var (cue, age) in shown)
            {
                if (cue.Kind == CueKind.Shortcut && cue.Label is { } label)
                {
                    float padding = 9 * unit, length = font.MeasureText(label) + 2 * padding;
                    float x = Math.Max(0, (width - length) / 2), y = height - 37 * unit;
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = new(16, 28, 44, (byte)(224 * Math.Min(1, (1 - age) * 5)));
                    canvas.DrawRoundRect(new SKRect(x, y, x + length, y + 27 * unit), 5 * unit, 5 * unit, paint);
                    paint.Color = SKColors.White.WithAlpha(paint.Color.Alpha);
                    canvas.DrawText(label, x + padding, y + 20 * unit, SKTextAlign.Left, font, paint);
                }
                else if (cue.Kind != CueKind.Shortcut)
                {
                    var (radius, opacity) = CueTimeline.Ring(age, unit);
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 3 * unit;
                    paint.Color = CueTimeline.Color(cue.Kind, opacity);
                    canvas.DrawCircle(cue.X, cue.Y, (float)radius, paint);
                    if (CueTimeline.Wheel(cue.Kind) is { } mark)
                    {
                        paint.Style = SKPaintStyle.Fill;
                        canvas.DrawText(mark, cue.X, cue.Y + 6 * unit, SKTextAlign.Center, font, paint);
                    }
                }
            }
            canvas.Flush();
        }
        finally { pin.Free(); }
    }

    public void Dispose()
    {
        font.Dispose();
        typeface.Dispose();
        paint.Dispose();
    }
}
