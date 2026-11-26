using System.Windows;
using System.Windows.Input;

namespace Yxrl.Views;

internal partial class DayDetailWindow : Window
{
    internal const int PreferredWidth = 328;
    private readonly Action closeDetails;

    public DayDetailWindow(Action closeDetails)
    {
        InitializeComponent();
        this.closeDetails = closeDetails;
    }

    public void SetDate(DayInfo day, int? week, string events)
    {
        DayHeading.Text = day.Date.ToString("yyyy年M月d日");
        DayWeek.Text = CalendarService.Weekday(day.Date) + $"  ·  第{day.Date.DayOfYear}天"
            + (week.HasValue ? $"  ·  第{week}周" : "");
        DayLunarFull.Text = day.LunarDate;
        DayGanzhi.Text = day.Ganzhi;
        DayEvents.Text = events;
        DayEvents.Visibility = events.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DayYiFull.Text = string.Join("、", day.Lunar.DayYi);
        DayJiFull.Text = string.Join("、", day.Lunar.DayJi);
    }

    private void CloseClick(object sender, RoutedEventArgs e) => closeDetails();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Escape or Key.Back)) return;
        e.Handled = true;
        closeDetails();
    }
}
