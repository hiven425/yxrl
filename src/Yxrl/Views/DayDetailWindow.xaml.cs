using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal partial class DayDetailWindow : Window
{
    internal const int PreferredWidth = 336;
    private readonly Action closeDetails;
    private DayInfo? lastDay;
    private int? lastWeek;

    public DayDetailWindow(Action closeDetails)
    {
        InitializeComponent();
        if (Theme.AppIcon is { } appIcon) Icon = appIcon;
        this.closeDetails = closeDetails;
        IsVisibleChanged += (_, e) => {
            if (e.NewValue is true && SystemParameters.ClientAreaAnimation && RootBorder.Child is UIElement content)
                content.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(130)) { FillBehavior = FillBehavior.Stop });
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyChrome();
    }

    internal void RefreshTheme()
    {
        ApplyChrome();
        if (lastDay is not null) SetDate(lastDay, lastWeek);
    }

    private void ApplyChrome()
    {
        if (Theme.ApplyWindowChrome(this, Native.BackdropKind.Acrylic, setBorder: true)) {
            RootBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceGlass");
            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.CornerRadius = new CornerRadius(0);
        } else {
            RootBorder.SetResourceReference(Border.BackgroundProperty, "Surface");
            RootBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.CornerRadius = new CornerRadius(12);
        }
    }

    public void SetDate(DayInfo day, int? week)
    {
        lastDay = day;
        lastWeek = week;
        TileMonth.Text = $"{day.Date.Month}月";
        TileDay.Text = day.Date.Day.ToString();
        DayHeading.Text = day.Date.ToString("yyyy年M月d日");
        DayWeek.Text = CalendarService.Weekday(day.Date) + $" · 第{day.Date.DayOfYear}天"
            + (week.HasValue ? $" · 第{week}周" : "");

        DayTags.Children.Clear();
        foreach (var fest in day.Festivals.Split('、', StringSplitOptions.RemoveEmptyEntries))
            DayTags.Children.Add(TagPill(fest, "Accent", "Hot"));
        if (day.Lunar.JieQi.Length > 0)
            DayTags.Children.Add(TagPill(day.Lunar.JieQi, "Accent", "Hot"));
        if (day.Mark.Length > 0)
            DayTags.Children.Add(TagPill(day.Mark == "休" ? "放假" : "调休上班",
                day.Mark == "休" ? "Weekend" : "Positive",
                day.Mark == "休" ? "RestFill" : "WorkFill"));
        DayTags.Visibility = DayTags.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        DayLunarFull.Text = "农历" + day.LunarDate;
        DayGanzhi.Text = day.Ganzhi;
        DayYiFull.Text = day.Lunar.DayYi.Count > 0 ? string.Join("、", day.Lunar.DayYi) : "—";
        DayJiFull.Text = day.Lunar.DayJi.Count > 0 ? string.Join("、", day.Lunar.DayJi) : "—";
    }

    private Border TagPill(string text, string fgKey, string bgKey) => new() {
        Background = (System.Windows.Media.Brush)FindResource(bgKey),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 2, 6, 2),
        Margin = new Thickness(0, 0, 5, 3),
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource(fgKey), FontWeight = FontWeights.Medium }
    };

    private void CloseClick(object sender, RoutedEventArgs e) => closeDetails();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Escape or Key.Back)) return;
        e.Handled = true;
        closeDetails();
    }
}
