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
    private readonly SettingsStore store;
    private readonly CalendarService calendar;
    private readonly WeatherService weather;
    private readonly MonthViewModel vm;
    private readonly Action openSettings;
    private DateOnly today;
    private bool presented;

    public CalendarWindow(SettingsStore store, CalendarService calendar, WeatherService weather, Action openSettings)
    {
        InitializeComponent();
        (this.store,this.calendar,this.weather,this.openSettings)=(store,calendar,weather,openSettings);
        vm=new(calendar); vm.Select(DateOnly.FromDateTime(DateTime.Now));
        Closing += (_,e) => { if (presented) { e.Cancel=true; Dismiss(); } };
    }
    public void Present(ClockAnchor? anchor, bool resetDate = true)
    {
        presented = false;
        if (resetDate) vm.Select(DateOnly.FromDateTime(DateTime.Now));
        ShowMonth();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var taskbar = anchor?.Taskbar ?? Native.FindWindow("Shell_TrayWnd", null);
        var info = Native.MonitorOf(taskbar==0 ? hwnd : taskbar);
        var work = anchor?.Work ?? info.Work;
        uint dpi = anchor?.Dpi ?? Native.GetDpiForWindow(hwnd);
        double scale = Math.Max(1,dpi / 96.0); int gap=(int)Math.Round(8*scale);
        int w = Math.Min((int)Math.Round(408*scale), work.Width-2*gap);
        int h = Math.Min((int)Math.Round(558*scale),work.Height-2*gap);
        var b = anchor?.Bounds;
        int x = Math.Clamp(b?.Right - w ?? work.Right-w-gap,work.Left+gap,work.Right-w-gap);
        int y = work.Bottom-h-gap;
        if (Native.GetWindowRect(taskbar,out var bar)) {
            if (bar.Width >= bar.Height) {
                bool atTop = bar.Top < (work.Top + work.Bottom) / 2;
                int edge = atTop ? Math.Max(bar.Bottom,info.Monitor.Top+bar.Height) : Math.Min(bar.Top,info.Monitor.Bottom-bar.Height);
                y = atTop ? Math.Max(work.Top,edge)+gap : Math.Min(work.Bottom,edge)-h-gap;
                y = Math.Clamp(y,work.Top+gap,work.Bottom-h-gap);
            } else {
                y=Math.Clamp((anchor?.Bounds.Bottom??work.Bottom)-h,work.Top+gap,work.Bottom-h-gap);
                x = bar.Left < (work.Left+work.Right)/2 ? Math.Max(work.Left,bar.Right)+gap : Math.Min(work.Right,bar.Left)-gap-w;
                x = Math.Clamp(x,work.Left+gap,work.Right-w-gap);
            }
        }
        Native.SetWindowPos(hwnd,Native.Topmost,x,y,w,h,Native.NoActivate);
        if (!IsVisible) Show();
        Native.SetWindowPos(hwnd,Native.Topmost,x,y,w,h,Native.Show);
        Native.SetForegroundWindow(hwnd); Activate(); Focus();
        presented=true;
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
        if (place is null) { WeatherLine.Visibility=Visibility.Collapsed; return; }
        var w=weather.Read(place);
        WeatherLine.Visibility=Visibility.Visible;
        WeatherLine.Text=w?.Current is { } reading ? $"{place.Name}  {reading.Condition} {reading.Temp:0}°  ·  {reading.ObservedAt.Replace('T',' ')}" : $"{place.Name}  天气待更新";
        WeatherLine.ToolTip=weather.Warning;
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
            r++; DaysGrid.RowDefinitions.Add(new(){Height=new GridLength(45)});
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
