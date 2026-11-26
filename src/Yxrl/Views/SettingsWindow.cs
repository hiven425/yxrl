using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal sealed class SettingsWindow : Window
{
    internal SettingsPanel Page { get; }

    public SettingsWindow(SettingsStore store, CalendarService calendar, WeatherService weather,
        Action applied, Action back, Action quit)
    {
        Title = "YXRL 设置";
        AutomationProperties.SetAutomationId(this, "YxrlSettingsWindow");
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "Surface");
        WindowStartupLocation = WindowStartupLocation.Manual;

        void ReturnToMonth() { Close(); back(); }
        Page = new SettingsPanel(store, calendar, weather, () => { applied(); ReturnToMonth(); }, ReturnToMonth, quit);
        Content = Page;
        Closed += (_, _) => Page.CancelSearch();
        KeyDown += (_, e) => {
            if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; ReturnToMonth(); }
        };
    }

    public void Present(ClockAnchor? anchor)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var taskbar = anchor?.Taskbar ?? Native.FindWindow("Shell_TrayWnd", null);
        var monitor = Native.MonitorOf(taskbar == 0 ? hwnd : taskbar);
        var work = anchor?.Work ?? monitor.Work;
        double scale = Math.Max(1, (anchor?.Dpi ?? Native.GetDpiForWindow(taskbar == 0 ? hwnd : taskbar)) / 96.0);
        int margin = (int)Math.Round(8 * scale);
        int width = Math.Min((int)Math.Round(810 * scale), work.Width - 2 * margin);
        int height = Math.Min((int)Math.Round(712 * scale), work.Height - 2 * margin);
        MinWidth = Math.Min(360, width / scale);
        MinHeight = Math.Min(320, height / scale);
        int left = work.Left + (work.Width - width) / 2, top = work.Top + (work.Height - height) / 2;
        Native.SetWindowPos(hwnd, 0, left, top, width, height, Native.NoZOrder | Native.NoActivate);
        Page.LoadDraft();
        Show();
        Native.SetWindowPos(hwnd, 0, left, top, width, height, Native.NoZOrder);
        BringForward();
    }

    public void BringForward()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        var target = OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible) ?? this;
        Native.SetForegroundWindow(new WindowInteropHelper(target).Handle);
        target.Activate();
    }
}
