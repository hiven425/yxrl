using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public CalendarWindow(SettingsStore store, CalendarService calendar, WeatherService weather, Action openSettings)
    {
        InitializeComponent();
        Width = DefaultSize.Width;
        Height = DefaultSize.Height;
        (this.store,this.calendar,this.weather,this.openSettings)=(store,calendar,weather,openSettings);
        vm=new(calendar); vm.Select(DateOnly.FromDateTime(DateTime.Now));
        Closing += (_,e) => { if (presented) { e.Cancel=true; Dismiss(); } };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ResizeMessage);
    }

    private nint ResizeMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmSizing && resizeArea.HasArea) {
            if (resizeStart is null && Native.GetWindowRect(hwnd, out var current)) resizeStart = current;
            var proposed = Marshal.PtrToStructure<PixelRect>(lParam);
            var bounded = proposed.Intersect(resizeArea);
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
    }

    public void Present(ClockAnchor? anchor, bool resetDate = true)
    {
        presented = false;
        if (resetDate) vm.Select(DateOnly.FromDateTime(DateTime.Now));
        ShowMonth();
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
        MaxWidth = resizeArea.Width / scale;
        MaxHeight = resizeArea.Height / scale;
        MinWidth = Math.Min(MinimumSize.Width, MaxWidth);
        MinHeight = Math.Min(MinimumSize.Height, MaxHeight);
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
    public void Dismiss() { presented=false; Hide(); }
    public void Finish() { presented=false; Close(); }
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (presented && store.CloseOnBlur) Dismiss();
    }
    private void SettingsClick(object sender,RoutedEventArgs e) => openSettings();
    private void ShowMonth()
    {
        Title="YXRL 月历"; MonthPage.Visibility=Visibility.Visible; DayPage.Visibility=Visibility.Collapsed;
        RenderMonth(); Tick(DateTime.Now);
    }
    public void Tick(DateTime now)
    {
        var date=DateOnly.FromDateTime(now);
        if (date != today) { if(vm.Selected==today) vm.Select(date); today=date; RenderMonth(); }
        TimePart.Text=ClockFormatter.Time(now,store.Current.Calendar.Hour12,false); SecondsPart.Text=now.ToString(":ss");
        AutomationProperties.SetName(LiveTime,TimePart.Text+SecondsPart.Text);
        TodayLine.Text=$"{date:yyyy年M月d日}  {CalendarService.Weekday(date)}  {calendar.Get(date).LunarDate}";
        var place=store.Current.City;
        var w=weather.Read(place);
        WeatherLine.Visibility=Visibility.Visible;
        WeatherLine.Text=WeatherReading.Summary(place, w);
        WeatherLine.ToolTip=weather.Warning is { } warning ? $"{WeatherLine.Text}\n{warning}" : WeatherLine.Text;
        WeatherCredit.Visibility=w?.UsesVisualCrossing == true ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PreviousClick(object sender,RoutedEventArgs e) => Move(-1);
    private void NextClick(object sender,RoutedEventArgs e) => Move(1);
    private void TodayClick(object sender,RoutedEventArgs e) => Select(DateOnly.FromDateTime(DateTime.Now));
    private void Move(int offset) { vm.Shift(offset); RenderMonth(); }
    private void Select(DateOnly day) { vm.Select(day); RenderMonth(); }
    private void DayBack(object sender,RoutedEventArgs e) => ShowMonth();
    private void ShowDay(DateOnly day)
    {
        Select(day); var d=vm.Detail;
        Title="YXRL 日详情"; MonthPage.Visibility=Visibility.Collapsed; DayPage.Visibility=Visibility.Visible;
        DayHeading.Text=day.ToString("yyyy年M月d日");
        DayWeek.Text=CalendarService.Weekday(day)+$"  ·  第{day.DayOfYear}天"+(store.Current.Calendar.Week(day) is int w?$"  ·  第{w}周":"");
        DayLunarFull.Text=d.LunarDate; DayGanzhi.Text=d.Ganzhi; DayEvents.Text=DetailFest.Text;
        DayYiFull.Text=string.Join("、",d.Lunar.DayYi); DayJiFull.Text=string.Join("、",d.Lunar.DayJi);
    }
    private void OnKey(object sender,KeyEventArgs e)
    {
        if (e.Key==Key.Escape) { Dismiss(); e.Handled=true; return; }
        if (DayPage.IsVisible) { if(e.Key==Key.Back){ShowMonth(); e.Handled=true;} return; }
        switch(e.Key) {
            case Key.Enter: case Key.Space:
                if(Keyboard.FocusedElement is Button btn && !AutomationProperties.GetAutomationId(btn).StartsWith("Day-")) return;
                ShowDay(vm.Selected); break;
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
                button.Click+=(_,_)=>ShowDay(date); Put(button,r,col+1);
            }
        }
        var d=vm.Detail; var num=opt.Week(d.Date);
        DetailTitle.Text=$"{d.Date:M月d日}  {CalendarService.Weekday(d.Date)}"+(num.HasValue?$"  ·  第{num}周":"");
        DetailLunar.Text=d.LunarDate+"  "+d.Ganzhi;
        DetailFest.Text=string.Join("  ", new[]{d.Festivals,d.Lunar.JieQi,opt.ShowAdjustments&&d.Mark.Length>0?"调休安排 · "+d.Mark:""}.Where(x=>x.Length>0));
        DetailFest.Visibility=DetailFest.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
}
