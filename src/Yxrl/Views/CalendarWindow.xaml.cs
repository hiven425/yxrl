using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal partial class CalendarWindow : Window
{
    private static readonly Size DefaultSize = new(408, 558);
    private static readonly Size MinimumSize = new(340, 450);
    private const int WmSizing = 0x0214, WmExitSizeMove = 0x0232;

    private readonly SettingsStore store;
    private readonly CalendarService calendar;
    private readonly WeatherService weather;
    private readonly MonthViewModel vm;
    private readonly Action openSettings;
    private DateOnly today;
    private bool presented;
    private Size preferredSize = DefaultSize;
    private PixelRect resizeArea;
    private PixelRect? resizeStart;
    private DayDetailWindow? details;
    private Button? selectedDayButton;
    private int detailWidth, detailGap;
    private PixelRect? boundsBeforeDetails;
    private Uri? weatherSourceLink;

    private PixelRect CalendarArea => detailWidth == 0 ? resizeArea
        : resizeArea with { Left = resizeArea.Left + detailWidth + detailGap };

    public CalendarWindow(SettingsStore store, CalendarService calendar, WeatherService weather,
        ReminderStore reminderStore, Action openSettings)
    {
        InitializeComponent();
        Width = DefaultSize.Width;
        Height = DefaultSize.Height;
        (this.store,this.calendar,this.weather,this.openSettings)=(store,calendar,weather,openSettings);
        this.reminderStore = reminderStore;
        vm=new(calendar); vm.Select(DateOnly.FromDateTime(DateTime.Now));
        Closing += (_, e) => {
            if (!presented) return;
            e.Cancel = true;
            Dispatcher.InvokeAsync(Dismiss);
        };
        LocationChanged += (_, _) => PositionDetails();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ResizeMessage);
    }

    private nint ResizeMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmSizing && CalendarArea.HasArea) {
            if (resizeStart is null && Native.GetWindowRect(hwnd, out var current)) resizeStart = current;
            var proposed = Marshal.PtrToStructure<PixelRect>(lParam);
            var bounded = proposed.Intersect(CalendarArea);
            if (bounded.HasArea && bounded != proposed) {
                Marshal.StructureToPtr(bounded, lParam, false);
                handled = true;
                return 1;
            }
        } else if (message == WmExitSizeMove && resizeStart is { } start) {
            resizeStart = null;
            if (Native.GetWindowRect(hwnd, out var end) && (end.Width != start.Width || end.Height != start.Height)) {
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                preferredSize = new(end.Width / scale, end.Height / scale);
                boundsBeforeDetails = null;
            }
        }
        return 0;
    }

    private void ScaleContent(object sender, SizeChangedEventArgs e)
    {
        double scale = Math.Min(e.NewSize.Width / DefaultSize.Width, e.NewSize.Height / DefaultSize.Height);
        if (scale <= 0) return;
        ContentScale.ScaleX = scale;
        ContentScale.ScaleY = scale;
        PositionDetails();
    }

    public void Present(ClockAnchor? anchor, bool resetDate = true)
    {
        presented = false;
        HideDetails();
        if (resetDate) vm.Select(DateOnly.FromDateTime(DateTime.Now));
        RenderMonth();
        Tick(DateTime.Now);
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var bounds = Placement(anchor, hwnd);
        if (!bounds.HasArea) return;
        Native.SetWindowPos(hwnd, Native.Topmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, Native.NoActivate);
        if (!IsVisible) Show();
        Native.SetWindowPos(hwnd, Native.Topmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, Native.Show);
        Native.SetForegroundWindow(hwnd); Activate(); Focus();
        presented=true;
    }

    private PixelRect Placement(ClockAnchor? anchor, nint hwnd)
    {
        var taskbar = anchor?.Taskbar ?? Native.FindWindow("Shell_TrayWnd", null);
        var monitorWindow = taskbar == 0 ? hwnd : taskbar;
        var info = Native.MonitorOf(monitorWindow);
        var work = anchor?.Work ?? info.Work;
        var monitor = anchor?.Monitor ?? info.Monitor;
        uint dpi = anchor?.Dpi ?? Native.GetDpiForWindow(monitorWindow);
        double scale = dpi == 0 ? 1 : dpi / 96.0;
        int gap = (int)Math.Round(8 * scale);
        var available = work;
        bool atTop = false, atLeft = false, vertical = false;
        if (Native.GetWindowRect(taskbar, out var bar) && bar.HasArea) {
            if (bar.Width >= bar.Height) {
                atTop = bar.Top < monitor.Center.Y;
                // Reserve the expanded taskbar even if it is auto-hidden when the window is reopened.
                available = atTop
                    ? work with { Top = Math.Max(work.Top, Math.Max(bar.Bottom, monitor.Top + bar.Height)) }
                    : work with { Bottom = Math.Min(work.Bottom, Math.Min(bar.Top, monitor.Bottom - bar.Height)) };
            } else {
                vertical = true;
                atLeft = bar.Left < monitor.Center.X;
                available = atLeft
                    ? work with { Left = Math.Max(work.Left, Math.Max(bar.Right, monitor.Left + bar.Width)) }
                    : work with { Right = Math.Min(work.Right, Math.Min(bar.Left, monitor.Right - bar.Width)) };
            }
        }
        if (!available.HasArea) available = work;
        if (!available.HasArea) return default;
        gap = Math.Min(gap, (Math.Min(available.Width, available.Height) - 1) / 2);
        resizeArea = new(available.Left + gap, available.Top + gap, available.Right - gap, available.Bottom - gap);
        SetSizeLimits(resizeArea, scale);
        int w = (int)Math.Round(Math.Clamp(preferredSize.Width, MinWidth, MaxWidth) * scale);
        int h = (int)Math.Round(Math.Clamp(preferredSize.Height, MinHeight, MaxHeight) * scale);
        int x = atLeft ? resizeArea.Left : resizeArea.Right - w;
        int y = atTop ? resizeArea.Top : resizeArea.Bottom - h;
        if (anchor is { } clock) {
            if (vertical) y = Math.Clamp(clock.Bounds.Bottom - h, resizeArea.Top, resizeArea.Bottom - h);
            else x = Math.Clamp(clock.Bounds.Right - w, resizeArea.Left, resizeArea.Right - w);
        }
        return new(x, y, x + w, y + h);
    }
    private void SetSizeLimits(PixelRect area, double scale)
    {
        MinWidth = Math.Min(MinimumSize.Width, area.Width / scale);
        MaxWidth = area.Width / scale;
        MinHeight = Math.Min(MinimumSize.Height, area.Height / scale);
        MaxHeight = area.Height / scale;
    }

    public void Dismiss() { presented=false; CloseReminderMenu(); HideDetails(); Hide(); }
    public void Finish() { presented=false; CloseReminderMenu(); HideDetails(); Close(); }
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!presented || !store.CloseOnBlur) return;
        // Activation can move between the two windows before the dispatcher returns to idle.
        Dispatcher.InvokeAsync(() => {
            if (presented && store.CloseOnBlur && !IsManagingReminder && !IsActive && details?.IsActive != true) Dismiss();
        }, DispatcherPriority.Background);
    }
    private void SettingsClick(object sender,RoutedEventArgs e) => openSettings();
    public void Tick(DateTime now)
    {
        var date=DateOnly.FromDateTime(now);
        if (date != today) { if(vm.Selected==today) vm.Select(date); today=date; RenderMonth(); }
        TimePart.Text=ClockFormatter.Time(now,store.Current.Calendar.Hour12,false); SecondsPart.Text=now.ToString(":ss");
        AutomationProperties.SetName(LiveTime,TimePart.Text+SecondsPart.Text);
        TodayLine.Text=$"{date:yyyy年M月d日}  {CalendarService.Weekday(date)}  {calendar.Get(date).LunarDate}";
        var place=store.Current.City;
        var w=weather.Read(place);
        RenderWeather(place, w, new DateTimeOffset(now));
    }
    private void RenderWeather(City? place, WeatherReading? reading, DateTimeOffset now)
    {
        WeatherLine.Text = WeatherReading.Summary(place, reading);
        WeatherLine.ToolTip = weather.Warning is { } warning ? $"{WeatherLine.Text}\n{warning}" : WeatherLine.Text;
        bool openMeteo = reading?.UsesOpenMeteo == true;
        bool visualCrossing = reading?.UsesVisualCrossing == true;
        weatherSourceLink = openMeteo ? WeatherAttributionLink.OpenMeteoUri
            : visualCrossing ? WeatherAttributionLink.VisualCrossingUri : null;
        WeatherAdornment.Visibility = weatherSourceLink is null ? Visibility.Collapsed : Visibility.Visible;
        WeatherCredit.Visibility = visualCrossing ? Visibility.Visible : Visibility.Collapsed;
        if (weatherSourceLink is null) return;

        var current = reading?.Current;
        var scene = WeatherAppearance.At(current, now);
        WeatherIcon.SetScene(scene);
        var label = WeatherAppearance.Label(scene, current);
        var credit = openMeteo ? "Open-Meteo" : VisualCrossing.Name;
        if (reading?.Air?.Source == "Open-Meteo") credit += " · CAMS";
        AutomationProperties.SetName(WeatherBadge, $"{label}，打开 {credit}");
        var tooltip = new List<string> { label };
        if (current?.Daylight is { } light) {
            if (light.Sunrise is { } rise && rise.Date == now.ToOffset(rise.Offset).Date) tooltip.Add($"日出 {rise:HH:mm}");
            if (light.Sunset is { } set && set.Date == now.ToOffset(set.Offset).Date) tooltip.Add($"日落 {set:HH:mm}");
        }
        WeatherBadge.ToolTip = string.Join(" · ", tooltip) + $"\n打开 {credit}";
    }
    private void WeatherBadgeClick(object sender, RoutedEventArgs e)
    {
        if (weatherSourceLink is { } uri) WeatherAttributionLink.Open(uri);
    }
    private void PreviousClick(object sender,RoutedEventArgs e) => Move(-1);
    private void NextClick(object sender,RoutedEventArgs e) => Move(1);
    private void TodayClick(object sender,RoutedEventArgs e) => Select(DateOnly.FromDateTime(DateTime.Now));
    private void Move(int offset) { vm.Shift(offset); RenderMonth(); }
    private void Select(DateOnly day) { vm.Select(day); RenderMonth(); }
    private void ToggleDetails(DateOnly day)
    {
        if (details?.IsVisible == true && day == vm.Selected) {
            CloseDetails();
            return;
        }
        if (day != vm.Selected) Select(day);
        if (details?.IsVisible != true) OpenDetails();
        selectedDayButton?.Focus();
    }

    private void OpenDetails()
    {
        if (!IsVisible || resizeArea.Width < 3 || !Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var month))
            return;
        if (details is null) {
            details = new(CloseDetails) { Owner = this };
            details.Deactivated += OnDeactivated;
            details.Closing += (_, e) => {
                if (!presented) return;
                e.Cancel = true;
                Dispatcher.InvokeAsync(CloseDetails);
            };
        }
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        detailGap = Math.Min((int)Math.Round(8 * scale), resizeArea.Width / 8);
        detailWidth = Math.Min((int)Math.Round(DayDetailWindow.PreferredWidth * scale), (resizeArea.Width - detailGap) / 2);
        var area = CalendarArea;
        var fitted = FitWidth(month, area, scale);
        if (fitted != month) boundsBeforeDetails = month;
        SetSizeLimits(area, scale);
        if (fitted != month) PlaceWindow(this, fitted);

        UpdateDetails(vm.Detail, store.Current.Calendar.Week(vm.Selected));
        PositionDetails();
        details.Show();
        PositionDetails();
    }

    private void UpdateDetails(DayInfo day, int? week)
    {
        details?.SetDate(day, week, DetailFest.Text);
        if (selectedDayButton is not null) AutomationProperties.SetItemStatus(selectedDayButton, "日详情已展开");
    }

    private void PositionDetails()
    {
        if (detailWidth == 0 || details is null || !Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var month))
            return;
        int right = month.Left - detailGap;
        PlaceWindow(details, new(right - detailWidth, month.Top, right, month.Bottom));
    }

    private static void PlaceWindow(Window window, PixelRect bounds)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        Native.SetWindowPos(hwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, Native.NoZOrder | Native.NoActivate);
    }

    private static PixelRect FitWidth(PixelRect month, PixelRect area, double scale)
    {
        int minimum = Math.Min((int)Math.Round(MinimumSize.Width * scale), area.Width);
        int width = Math.Clamp(month.Width, minimum, area.Width);
        int left = Math.Clamp(month.Left, area.Left, area.Right - width);
        return month with { Left = left, Right = left + width };
    }

    private void CloseDetails()
    {
        bool detailHadFocus = details?.IsActive == true;
        HideDetails();
        if (detailHadFocus && IsVisible) Activate();
        if (IsActive) selectedDayButton?.Focus();
    }

    private void HideDetails()
    {
        if (detailWidth == 0) return;
        detailWidth = 0;
        details?.Hide();
        if (selectedDayButton is not null) AutomationProperties.SetItemStatus(selectedDayButton, "");
        var restore = boundsBeforeDetails;
        boundsBeforeDetails = null;
        if (!presented || !IsVisible) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        bool located = Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var current);
        SetSizeLimits(resizeArea, scale);
        if (!located) return;
        var bounds = FitWidth(restore ?? current, resizeArea, scale);
        if (bounds != current) PlaceWindow(this, bounds);
    }
    private void OnKey(object sender,KeyEventArgs e)
    {
        if (IsManagingReminder) return;
        if (e.Key == Key.Escape) {
            if (details?.IsVisible == true) CloseDetails();
            else Dismiss();
            e.Handled = true;
            return;
        }
        switch(e.Key) {
            case Key.Enter: case Key.Space:
                if(Keyboard.FocusedElement is Button btn && !AutomationProperties.GetAutomationId(btn).StartsWith("Day-")) return;
                ToggleDetails(vm.Selected); break;
            case Key.Left: Select(vm.Selected.AddDays(-1)); break; case Key.Right: Select(vm.Selected.AddDays(1)); break;
            case Key.Up: Select(vm.Selected.AddDays(-7)); break; case Key.Down: Select(vm.Selected.AddDays(7)); break;
            case Key.PageUp: Move(-1); break; case Key.PageDown: Move(1); break;
            case Key.Home: Select(DateOnly.FromDateTime(DateTime.Now)); break; default:return;
        }
        e.Handled=true;
    }
    private Brush ColorOf(string key) => (Brush)FindResource(key);
    private void RenderMonth()
    {
        bool restoreFocus = selectedDayButton?.IsKeyboardFocused == true;
        selectedDayButton = null;
        var opt=store.Current.Calendar;
        MonthTitle.Text=vm.Month.ToString("yyyy年M月");
        Previous.IsEnabled=vm.Month>CalendarService.First;
        Next.IsEnabled=vm.Month.AddMonths(1)<=CalendarService.Last;
        DaysGrid.Children.Clear(); DaysGrid.ColumnDefinitions.Clear(); DaysGrid.RowDefinitions.Clear();
        DaysGrid.ColumnDefinitions.Add(new(){Width=new GridLength(opt.ShowWeeks?23:0)});
        for(int i=0;i<7;i++) DaysGrid.ColumnDefinitions.Add(new());
        DaysGrid.RowDefinitions.Add(new(){Height=new GridLength(24)});
        void Put(UIElement element,int row,int column) { Grid.SetRow(element,row); Grid.SetColumn(element,column); DaysGrid.Children.Add(element); }
        TextBlock Text(string label,double size,string color) => new(){Text=label,FontSize=size,Foreground=ColorOf(color),
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
        if (opt.ShowWeeks) Put(Text("周",11,"Muted"),0,0);
        for(int col=0;col<7;col++) Put(Text("日一二三四五六"[(opt.WeekStart+col)%7].ToString(),12,"Muted"),0,col+1);
        int r=0;
        foreach(var row in vm.Weeks(opt,DateOnly.FromDateTime(DateTime.Now))) {
            r++; DaysGrid.RowDefinitions.Add(new(){Height=new GridLength(1, GridUnitType.Star)});
            if(row.Number is { } week) Put(Text(week.ToString(),11,"Muted"),r,0);
            for(int col=0;col<7;col++) {
                if(row.Days[col] is not { } cell) continue;
                var info=cell.Day; var date=info.Date;
                string color=cell.IsToday?"OnSelected":cell.Outside?"Muted":date.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Saturday?"Weekend":"Ink";
                var number=Text(date.Day.ToString(),15,color); number.FontWeight=FontWeights.SemiBold;
                var content=new StackPanel {VerticalAlignment=VerticalAlignment.Center, Children={number}};
                if(opt.ShowLunar) content.Children.Add(Text(info.Secondary,11,cell.IsToday?"OnSelected":
                    info.Festivals.Length>0 || info.Lunar.JieQi.Length>0 ? "Accent":"Muted"));
                var host=new Grid {Children={content}};
                if(opt.ShowAdjustments && info.Mark.Length>0) {
                    var mark=Text(info.Mark,10,info.Mark=="休"?"Weekend":"Positive");
                    host.Children.Add(new Border {Child=mark,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,
                        Background=ColorOf(info.Mark=="休"?"RestFill":"WorkFill"),CornerRadius=new CornerRadius(3),Padding=new Thickness(1,0,1,0)});
                }
                var button=new Button {Content=host,Margin=new Thickness(1),Padding=new Thickness(1),Focusable=true,
                    HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,
                    IsTabStop=cell.IsSelected, Opacity=cell.Outside ? 0.75 : 1,
                    Background=cell.IsToday?ColorOf("Selected"):cell.IsSelected?ColorOf("Hot"):Brushes.Transparent,
                    BorderBrush=cell.IsSelected?ColorOf("Accent"):Brushes.Transparent,ToolTip=$"{date:yyyy-MM-dd} {info.LunarDate} {info.Festivals}"};
                if(cell.IsToday && cell.IsSelected) host.Children.Insert(0,new Border {BorderBrush=ColorOf("OnSelected"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5)});
                AutomationProperties.SetName(button,$"{date:yyyy年M月d日} {info.Festivals} {info.Mark}".Trim());
                AutomationProperties.SetAutomationId(button,$"Day-{date:yyyy-MM-dd}");
                AttachReminderMenu(button,host,date,cell.IsToday);
                if (cell.IsSelected) selectedDayButton = button;
                button.Click+=(_,_)=>ToggleDetails(date); Put(button,r,col+1);
            }
        }
        var d=vm.Detail; var num=opt.Week(d.Date);
        DetailTitle.Text=$"{d.Date:M月d日}  {CalendarService.Weekday(d.Date)}"+(num.HasValue?$"  ·  第{num}周":"");
        DetailLunar.Text=d.LunarDate+"  "+d.Ganzhi;
        DetailFest.Text=string.Join("  ", new[]{d.Festivals,d.Lunar.JieQi,opt.ShowAdjustments&&d.Mark.Length>0?"调休安排 · "+d.Mark:""}.Where(x=>x.Length>0));
        DetailFest.Visibility=DetailFest.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        if (details?.IsVisible == true) UpdateDetails(d, num);
        if (restoreFocus) selectedDayButton?.Focus();
    }
}
