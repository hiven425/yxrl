using System.Windows;
using System.Windows.Interop;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal partial class CalendarWindow
{
    private readonly PointerLeaveTimeout leaveTimeout = new();
    private nint calendarHandle;
    private bool resizing;

    public bool IsPinned => PinButton.IsChecked == true;
    private bool CanAutoDismiss => presented && IsVisible && !IsPinned && !resizing && !IsManagingReminder && CaptureEntry.ContextMenu?.IsOpen != true;

    private void PinChanged(object sender, RoutedEventArgs e) => leaveTimeout.Reset();

    public void PollPointer()
    {
        bool outside = CanAutoDismiss && Native.GetCursorPos(out var point) && !OverCalendar(point);
        if (leaveTimeout.ShouldDismiss(Environment.TickCount64, outside)) Dismiss();
    }

    public void HandleDesktopClick(PixelPoint point)
    {
        if (CanAutoDismiss && store.CloseOnOutsideClick && !OverCalendar(point)) Dismiss();
    }

    private bool OverCalendar(PixelPoint point)
    {
        var hit = Native.GetAncestor(Native.WindowFromPoint(point), 2);
        return hit != 0 && (hit == calendarHandle ||
            (details?.IsVisible == true && hit == new WindowInteropHelper(details).Handle));
    }
}
