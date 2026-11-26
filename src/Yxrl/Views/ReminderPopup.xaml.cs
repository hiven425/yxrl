using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Interop;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal sealed record ReminderNotice(string Title, string Details);

internal partial class ReminderPopup : Window
{
    private readonly ObservableCollection<ReminderNotice> entries = [];

    public ReminderPopup()
    {
        InitializeComponent();
        Notices.ItemsSource = entries;
    }

    public void Append(IEnumerable<ReminderNotice> notices, nint taskbar)
    {
        foreach (var notice in notices) entries.Add(notice);

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var monitorWindow = taskbar == 0 ? hwnd : taskbar;
        var info = Native.MonitorOf(monitorWindow);
        var area = info.Work;
        if (taskbar != 0 && Native.GetWindowRect(taskbar, out var bar) &&
            bar.Width >= bar.Height && bar.Top >= info.Monitor.Center.Y) {
            // Reserve the visible taskbar height even when Windows auto-hides it.
            area = area with { Bottom = Math.Min(area.Bottom, info.Monitor.Bottom - bar.Height) };
        }
        double dpiScale = Math.Max(1, Native.GetDpiForWindow(monitorWindow) / 96.0);
        int pad = (int)Math.Round(12 * dpiScale);
        int width = Math.Min((int)Math.Round(344 * dpiScale), Math.Max(1, area.Width - 2 * pad));
        int height = Math.Min((int)Math.Round((166 + Math.Min(3, entries.Count - 1) * 62) * dpiScale),
            Math.Max(1, area.Height - 2 * pad));
        int x = area.Right - width - pad, y = area.Bottom - height - pad;
        Native.SetWindowPos(hwnd, Native.Topmost, x, y, width, height, Native.NoActivate);
        if (!IsVisible) Show();
        Native.SetWindowPos(hwnd, Native.Topmost, x, y, width, height, Native.NoActivate);
    }

    private void Dismiss(object sender, RoutedEventArgs e) => Close();
}
