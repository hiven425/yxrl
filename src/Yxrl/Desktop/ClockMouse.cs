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
    private readonly Action<PixelPoint> desktopClick;
    private Dispatcher? pump;
    private nint hook;
    private int error;
    private ClockAnchor[] anchors = [];
    private ClockAnchor? pressed;
    private bool observeOutsideClicks;

    public ClockMouse(Action<ClockAnchor> clicked, Action<PixelPoint> desktopClick)
    {
        this.clicked = clicked;
        this.desktopClick = desktopClick;
        callback = OnMouse;
        thread = new Thread(Run) { IsBackground = true, Name = "YXRL clock input" };
        thread.Start(); ready.Wait();
        if (hook == 0) { ready.Dispose(); throw new Win32Exception(error,"无法接收任务栏时钟点击"); }
    }
    public void Update(ClockAnchor[] visible, bool observeClicks)
    {
        Volatile.Write(ref anchors,visible);
        Volatile.Write(ref observeOutsideClicks,observeClicks);
    }
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
        if (code != 0) return Native.CallNextHookEx(0,code,msg,data);
        const int leftDown = 0x201, leftUp = 0x202, rightDown = 0x204, middleDown = 0x207, xDown = 0x20B;
        var visible = Volatile.Read(ref anchors);
        bool clockGesture = msg == leftDown && visible.Length > 0 || msg == leftUp && pressed is not null;
        bool observe = Volatile.Read(ref observeOutsideClicks) && (msg == leftDown || msg == rightDown ||
            msg == middleDown || msg == xDown);
        if (clockGesture || observe) {
            var point = Marshal.PtrToStructure<PixelPoint>(data);
            if (clockGesture) {
                var hit = Array.Find(visible, a => a.Bounds.Contains(point) && Native.OnTaskbar(a.Taskbar,point)
                    && Native.GetWindowRect(a.Taskbar,out var bar) && bar == a.Bar);
                if (msg == leftDown && hit is not null) { pressed = hit; return 1; }
                if (msg == leftUp && pressed is { } start) {
                    pressed = null;
                    if (hit?.Taskbar == start.Taskbar) clicked(hit);
                    return 1;
                }
            }
            if (observe) desktopClick(point);
        }
        return Native.CallNextHookEx(0,code,msg,data);
    }
    public void Dispose()
    {
        Update([],false);
        pump?.BeginInvokeShutdown(DispatcherPriority.Send);
        thread.Join(); ready.Dispose();
    }
}
