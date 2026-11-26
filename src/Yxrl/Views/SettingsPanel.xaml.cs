using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Yxrl.Views;

internal partial class SettingsPanel : UserControl
{
    private readonly SettingsStore store;
    private readonly CalendarService calendar;
    private readonly WeatherService weather;
    private readonly Action saved,back,quit;
    private City? city;
    private sealed record StyleTile(string Id, string Name, string Sample);
    private bool loading;
    private CancellationTokenSource? search;
    public string Status {get=>StatusText.Text; set=>StatusText.Text=value;}
    public SettingsPanel(SettingsStore store,CalendarService calendar,WeatherService weather,Action saved,Action back,Action quit)
    {
        (this.store,this.calendar,this.weather,this.saved,this.back,this.quit)=(store,calendar,weather,saved,back,quit);
        loading=true; InitializeComponent();
        Target.ItemsSource=calendar.Rules;
        WeekStart.ItemsSource="日一二三四五六".Select(x=>"星期"+x).ToArray();
        loading=false;
    }
    public void LoadDraft()
    {
        loading=true;
        var s=store.Current; var c=s.Calendar;
        EnabledClock.IsChecked=s.Clock.Enabled;
        var now=DateTime.Now;
        var tiles=ClockFormatter.Styles.Where(style=>style.Id!="custom").Select(style=>{
            var preview=ClockFormatter.Render(now,s with {Clock=s.Clock with {Preset=style.Id}},calendar,weather.Read(s.City));
            return new StyleTile(style.Id,style.Name,preview.Upper);
        }).ToList();
        Preset.ItemsSource=tiles; Preset.SelectedItem=tiles.Find(x=>x.Id==s.Clock.Preset);
        Upper.Text=s.Clock.Top; Lower.Text=s.Clock.Bottom; Target.SelectedItem=calendar.Rules.FirstOrDefault(x=>x.Id==s.Clock.Target);
        LunarBox.IsChecked=c.ShowLunar; MarkBox.IsChecked=c.ShowAdjustments; OutsideBox.IsChecked=c.ShowOutside;
        WeekBox.IsChecked=c.ShowWeeks; Hour12.IsChecked=c.Hour12; WeekStart.SelectedIndex=c.WeekStart;
        RangeBox.IsChecked=c.From.HasValue; RangeFrom.SelectedDate=c.From?.ToDateTime(TimeOnly.MinValue);
        RangeTo.SelectedDate=c.To?.ToDateTime(TimeOnly.MinValue); city=s.City; CityLabel.Text=city?.Label??"尚未选择城市";
        Query.Text=""; Cities.ItemsSource=null; Cities.Visibility=Visibility.Collapsed; SearchStatus.Text="";
        CalExpander.IsExpanded=false; CityExpander.IsExpanded=false;
        ErrorLine.Text=store.ReadError??""; loading=false; UpdateGroups(); Tick(DateTime.Now);
    }
    private Preferences Draft(bool validateRange)
    {
        var preset=(Preset.SelectedItem as StyleTile)?.Id ?? "custom";
        var clock=new ClockOptions(EnabledClock.IsChecked==true,preset,
            preset=="custom"?Upper.Text:store.Current.Clock.Top, preset=="custom"?Lower.Text:store.Current.Clock.Bottom,
            (Target.SelectedItem as FestivalRule)?.Id);
        DateOnly? from=null,to=null;
        if (RangeBox.IsChecked==true) {
            if (RangeFrom.SelectedDate is { } a && RangeTo.SelectedDate is { } b) { from=DateOnly.FromDateTime(a); to=DateOnly.FromDateTime(b); }
            else if(validateRange) throw new FormatException("请选择周数的开始和结束日期。");
        }
        var cal=new CalendarOptions(LunarBox.IsChecked==true,MarkBox.IsChecked==true,OutsideBox.IsChecked==true,
            Hour12.IsChecked==true,WeekBox.IsChecked==true,Math.Max(0,WeekStart.SelectedIndex),from,to);
        return new(clock,cal,city);
    }
    private void DraftChanged(object sender,RoutedEventArgs e) { if (!loading && IsLoaded) { UpdateGroups(); Tick(DateTime.Now); } }
    private void UpdateGroups()
    {
        var id=(Preset.SelectedItem as StyleTile)?.Id ?? "custom";
        CustomPick.SetResourceReference(BorderBrushProperty,id=="custom"?"Accent":"Line");
        CustomGroup.Visibility=id=="custom"?Visibility.Visible:Visibility.Collapsed;
        CountdownGroup.Visibility=id is "countdown" or "custom"?Visibility.Visible:Visibility.Collapsed;
        RangeBox.IsEnabled=WeekBox.IsChecked==true;
        RangeGroup.Visibility=RangeBox.IsChecked==true && WeekBox.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
    }
    private void ChooseCustom(object sender,RoutedEventArgs e) { Preset.SelectedItem=null; UpdateGroups(); Tick(DateTime.Now); Upper.Focus(); }
    public void Tick(DateTime now)
    {
        if(loading) return;
        try {
            var draft=Draft(false); var value=ClockFormatter.Render(now,draft,calendar,weather.Read(city));
            PreviewTop.Text=value.Upper; PreviewBottom.Text=value.Lower;
        } catch(FormatException ex) { PreviewTop.Text="格式有误"; PreviewBottom.Text=ex.Message; }
    }
    private void SaveClick(object sender,RoutedEventArgs e)
    {
        try { store.Save(Draft(true)); CancelSearch(); saved(); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or FormatException) { ErrorLine.Text=ex.Message; }
    }
    private async void SearchClick(object sender,RoutedEventArgs e)
    {
        CancelSearch(); var query=Query.Text.Trim();
        Cities.Visibility=Visibility.Collapsed;
        if(query.Length < 2) { SearchStatus.Text="请输入至少两个字符的城市名。"; return; }
        using var source=new CancellationTokenSource(); search=source; SearchStatus.Text="正在搜索…";
        try {
            var result=await weather.Search(query,source.Token);
            if(source.IsCancellationRequested)return;
            Cities.ItemsSource=result; Cities.Visibility=result.Count>0?Visibility.Visible:Visibility.Collapsed;
            SearchStatus.Text=result.Count==0?"没有找到城市，试试拼音。":"选择城市后点击「应用设置」。";
        } catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) {
            if(!source.IsCancellationRequested) SearchStatus.Text="城市搜索暂时不可用，请稍后重试。";
        } finally { if(search==source)search=null; }
    }
    public void CancelSearch() { search?.Cancel(); search=null; }
    private void CitySelected(object sender,SelectionChangedEventArgs e) { if(Cities.SelectedItem is City choice) {city=choice; CityLabel.Text=choice.Label; Tick(DateTime.Now);} }
    private void ClearCity(object sender,RoutedEventArgs e) { CancelSearch(); city=null; Cities.ItemsSource=null; Cities.Visibility=Visibility.Collapsed; SearchStatus.Text=""; CityLabel.Text="尚未选择城市"; Tick(DateTime.Now); }
    private void GoBack(object sender,RoutedEventArgs e) { CancelSearch(); back(); }
    private void ExitClick(object sender,RoutedEventArgs e) => quit();
    private void ReadLicenses(object sender,RoutedEventArgs e)
    {
        if(LicenseText.Text.Length!=0) return;
        string[] resources=["License.Lunar","License.Wpf","License.Dotnet","License.Notices"];
        LicenseText.Text=string.Join("\n\n",resources.Select(name=>{
            using var stream=typeof(App).Assembly.GetManifestResourceStream(name)!;
            using var reader=new StreamReader(stream);
            return name+"\n"+reader.ReadToEnd();
        }));
    }
}
