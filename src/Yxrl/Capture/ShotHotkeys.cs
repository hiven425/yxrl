using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Yxrl.Capture;

internal sealed class ShotBindingException(ShotAction action, string message) : FormatException(message)
{
    public ShotAction Action { get; } = action;
}

internal sealed class ShotHotkeys : IDisposable
{
    [DllImport("user32", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint mods, uint key);
    [DllImport("user32")]
    private static extern bool UnregisterHotKey(nint window, int id);
    private readonly HwndSource source;
    private readonly Action<ShotAction> dispatch;
    private readonly Dictionary<ShotChord, int> active = new();
    private Dictionary<ShotChord, ShotAction> actions = new();
    private int nextId;
    public Action<ShotChord>? Recording { get; set; }
    public Dictionary<ShotAction, string> Errors { get; } = new();

    public ShotHotkeys(Action<ShotAction> dispatch)
    {
        this.dispatch = dispatch;
        source = new HwndSource(new HwndSourceParameters("YXRL 截图快捷键") { ParentWindow = -3, Width = 0, Height = 0, WindowStyle = 0 });
        source.AddHook(Message);
    }

    public void Apply(CaptureOptions options, bool strict)
    {
        options.Validate();
        var wanted = new Dictionary<ShotChord, ShotAction>();
        if (options.Enabled)
            foreach (var action in new[]
            {
                ShotAction.Start,
                ShotAction.Clipboard
            })
                if (ShotChord.TryParse(options.Shortcuts[action], out var k))
                    wanted.Add(k, action);
        var acquired = new Dictionary<ShotChord, int>();
        var failures = new Dictionary<ShotAction, string>();
        foreach (var (chord, action) in wanted)
        {
            if (active.ContainsKey(chord))
                continue;
            // App hotkey identifiers are limited to the range 0..0xbfff.
            do
            {
                nextId = nextId % 0xbfff + 1;
            }
            while (active.ContainsValue(nextId) || acquired.ContainsValue(nextId));
            if (RegisterHotKey(source.Handle, nextId, (uint)chord.Mods | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(chord.Key)))
            {
                acquired.Add(chord, nextId);
                continue;
            }

            int code = Marshal.GetLastWin32Error();
            var message = $"「{CaptureOptions.Label(action)}」{chord} 注册失败" + (code == 1409 ? "，已被占用。" : $"（Windows {code}）。");
            if (strict && (!actions.TryGetValue(chord, out var original) || original != action))
            {
                foreach (var id in acquired.Values)
                    UnregisterHotKey(source.Handle, id);
                throw new ShotBindingException(action, message);
            }

            failures[action] = message;
        }

        foreach (var chord in active.Keys.Except(wanted.Keys).ToArray())
        {
            UnregisterHotKey(source.Handle, active[chord]);
            active.Remove(chord);
        }

        foreach (var pair in acquired)
            active.Add(pair.Key, pair.Value);
        actions = wanted;
        Errors.Clear();
        foreach (var pair in failures)
            Errors.Add(pair.Key, pair.Value);
    }

    private nint Message(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != 0x0312)
            return 0;
        foreach (var (chord, id) in active)
        {
            if (id != (int)wParam)
                continue;
            handled = true;
            if (Recording is { } capture)
                capture(chord);
            else if (actions.TryGetValue(chord, out var action))
                source.Dispatcher.BeginInvoke(() => dispatch(action));
            break;
        }

        return 0;
    }

    public void Dispose()
    {
        foreach (var id in active.Values)
            UnregisterHotKey(source.Handle, id);
        active.Clear();
        source.RemoveHook(Message);
        source.Dispose();
    }
}
