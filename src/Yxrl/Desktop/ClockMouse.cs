using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Yxrl.Desktop;

// Explorer keeps its normal right button, wheel and neighboring controls. Only a left
// gesture started over a currently exposed native time control belongs to this app.
internal sealed class ClockMouse : IDisposable
{
    private readonly Native.MouseProc callback;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly Action<ClockAnchor> clicked;
    private Dispatcher? pump;
    private nint hook;
    private int error;
    private ClockAnchor[] anchors = [];
    private ClockAnchor? pressed;

    public ClockMouse(Action<ClockAnchor> clicked)
    {
        this.clicked = clicked;
        callback = OnMouse;
        thread = new Thread(Run) { IsBackground = true, Name = "YXRL clock input" };
        thread.Start(); ready.Wait();
        if (hook == 0) { ready.Dispose(); throw new Win32Exception(error,"无法接收任务栏时钟点击"); }
    }
    public void Update(ClockAnchor[] visible) => Volatile.Write(ref anchors,visible);
    private void Run()
    {
        pump = Dispatcher.CurrentDispatcher;
        hook = Native.SetWindowsHookEx(14,callback,Native.GetModuleHandle(null),0);
        if (hook == 0) error = Marshal.GetLastWin32Error();
        ready.Set();
        if (hook == 0) return;
        try { Dispatcher.Run(); }
        finally { Native.UnhookWindowsHookEx(hook); }
    }
    private nint OnMouse(int code, nint msg, nint data)
    {
        if (code == 0 && (msg == 0x201 || msg == 0x202)) {
            if (msg == 0x202 && pressed is null) return Native.CallNextHookEx(0,code,msg,data);
            var point = Marshal.PtrToStructure<PixelPoint>(data);
            var hit = Array.Find(Volatile.Read(ref anchors), a => a.Bounds.Contains(point) && Native.OnTaskbar(a.Taskbar,point)
                && Native.GetWindowRect(a.Taskbar,out var bar) && bar == a.Bar);
            if (msg == 0x201 && hit is not null) { pressed = hit; return 1; }
            if (msg == 0x202 && pressed is { } start) {
                pressed = null;
                if (hit?.Taskbar == start.Taskbar) clicked(hit);
                return 1;
            }
        }
        return Native.CallNextHookEx(0,code,msg,data);
    }
    public void Dispose()
    {
        Update([]);
        pump?.BeginInvokeShutdown(DispatcherPriority.Send);
        thread.Join(); ready.Dispose();
    }
}
