using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Yxrl.Views;

internal partial class SettingsPanel : UserControl
{
    private readonly SettingsStore store;
    private readonly CalendarService calendar;
    private readonly WeatherService weather;
    private readonly Action saved, back, quit;
    private readonly ClockStyleGallery gallery = new();
    private City? city;
    private bool loading = true;
    private string? saveError;
    private CancellationTokenSource? search;
    public string Status { get => StatusText.Text; set => StatusText.Text = value; }

    public static readonly DependencyProperty GalleryColumnsProperty =
        DependencyProperty.Register(nameof(GalleryColumns), typeof(int), typeof(SettingsPanel), new PropertyMetadata(3));
    public int GalleryColumns => (int)GetValue(GalleryColumnsProperty);
    public static readonly DependencyProperty NavigationColumnsProperty =
        DependencyProperty.Register(nameof(NavigationColumns), typeof(int), typeof(SettingsPanel), new PropertyMetadata(1));
    public int NavigationColumns => (int)GetValue(NavigationColumnsProperty);

    public SettingsPanel(SettingsStore store, CalendarService calendar, WeatherService weather, Action saved, Action back, Action quit)
    {
        (this.store, this.calendar, this.weather, this.saved, this.back, this.quit) = (store, calendar, weather, saved, back, quit);
        InitializeComponent();
        Preset.ItemsSource = gallery.Tiles;
        Target.ItemsSource = calendar.Rules;
        WeekStart.ItemsSource = "日一二三四五六".Select(x => "星期" + x).ToArray();
    }

    public void LoadDraft()
    {
        loading = true;
        var s = store.Current;
        var c = s.Calendar;
        EnabledClock.IsChecked = s.Clock.Enabled;
        Preset.SelectedItem = gallery.Tiles.FirstOrDefault(x => x.Id == s.Clock.Preset);
        Upper.Text = s.Clock.Top;
        Lower.Text = s.Clock.Bottom;
        Target.SelectedItem = calendar.Rules.FirstOrDefault(x => x.Id == s.Clock.Target);
        LunarBox.IsChecked = c.ShowLunar;
        MarkBox.IsChecked = c.ShowAdjustments;
        OutsideBox.IsChecked = c.ShowOutside;
        WeekBox.IsChecked = c.ShowWeeks;
        Hour12.IsChecked = c.Hour12;
        WeekStart.SelectedIndex = c.WeekStart;
        RangeBox.IsChecked = c.From.HasValue;
        RangeFrom.SelectedDate = c.From?.ToDateTime(TimeOnly.MinValue);
        RangeTo.SelectedDate = c.To?.ToDateTime(TimeOnly.MinValue);
        city = s.City;
        DirectWeather.IsChecked = s.DirectWeatherConnection;
        CityLabel.Text = city?.Label ?? "尚未选择城市";
        ExampleMode.IsChecked = true;
        Nav.SelectedIndex = 0;
        loading = false;
        UpdateSection();
        RefreshDraft();
    }

    private Preferences Draft(bool validateRange)
    {
        var preset = (Preset.SelectedItem as ClockStyleTile)?.Id ?? "custom";
        var clock = new ClockOptions(EnabledClock.IsChecked == true, preset,
            preset == "custom" ? Upper.Text : store.Current.Clock.Top,
            preset == "custom" ? Lower.Text : store.Current.Clock.Bottom, (Target.SelectedItem as FestivalRule)?.Id);
        DateOnly? from = null, to = null;
        if (RangeBox.IsChecked == true) {
            if (RangeFrom.SelectedDate is { } a && RangeTo.SelectedDate is { } b) {
                from = DateOnly.FromDateTime(a); to = DateOnly.FromDateTime(b);
            } else if (validateRange) throw new FormatException("请在「日历设置」中填写完整的周数起止日期。");
        }
        var cal = new CalendarOptions(LunarBox.IsChecked == true, MarkBox.IsChecked == true, OutsideBox.IsChecked == true,
            Hour12.IsChecked == true, WeekBox.IsChecked == true, Math.Max(0, WeekStart.SelectedIndex), from, to);
        return new(clock, cal, city, DirectWeather.IsChecked == true);
    }

    private void SectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        UpdateSection();
        Tick(DateTime.Now);
    }

    private void UpdateSection()
    {
        if (Nav.SelectedItem is not ListBoxItem selected) return;
        PageTitle.Text = AutomationProperties.GetName(selected);
        ClockPage.Visibility = Nav.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        CalendarPage.Visibility = Nav.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        WeatherPage.Visibility = Nav.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = Nav.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        LivePreview.Visibility = ClockPage.Visibility;
        BodyScroll.ScrollToTop();
    }

    private void PanelResized(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        bool compact = width < 690;
        DockPanel.SetDock(Side, compact ? Dock.Top : Dock.Left);
        Side.Width = compact ? double.NaN : 140;
        Side.Padding = compact ? new Thickness(5, 2, 5, 2) : new Thickness(6, 16, 6, 8);
        Side.BorderThickness = compact ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
        Body.Margin = width < 450 ? new Thickness(12, 10, 12, 10) : new Thickness(21, 15, 21, 14);
        SetValue(NavigationColumnsProperty, compact ? width < 475 ? 2 : 4 : 1);
        FormatFields.Columns = width < 440 ? 1 : 2;
    }

    private void GalleryResized(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        SetValue(GalleryColumnsProperty, width >= 505 ? 3 : width >= 310 ? 2 : 1);
    }

    private void PresetChanged(object sender, SelectionChangedEventArgs e) => DraftChanged(sender, e);
    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        if (ExampleMode.IsChecked == true) gallery.ShowExamples();
        Tick(DateTime.Now);
    }
    private void DraftChanged(object sender, RoutedEventArgs e)
    {
        if (!loading) RefreshDraft();
    }
    private void RefreshDraft()
    {
        saveError = null;
        bool custom = Preset.SelectedItem is null;
        CustomPick.IsChecked = custom;
        CountdownGroup.Visibility = custom || (Preset.SelectedItem as ClockStyleTile)?.Id == "countdown" ? Visibility.Visible : Visibility.Collapsed;
        RangeBox.IsEnabled = WeekBox.IsChecked == true;
        RangeGroup.Visibility = RangeBox.IsEnabled && RangeBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Tick(DateTime.Now);
    }
    private void ChooseCustom(object sender, RoutedEventArgs e)
    {
        if (!loading && Preset.SelectedItem is not null) Preset.SelectedItem = null;
    }
    private void FormatEdited(object sender, TextChangedEventArgs e)
    {
        if (loading) return;
        Preset.SelectedItem = null;
        RefreshDraft();
    }
    private void ShowFormatGuide(object sender, RoutedEventArgs e)
    {
        var guide = new ClockFormatGuide { Owner = Window.GetWindow(this) };
        guide.ShowDialog();
    }

    public void Tick(DateTime now)
    {
        if (loading) return;
        var draft = Draft(false);
        var reading = weather.Read(city);
        if (WeatherPage.IsVisible) {
            WeatherData.Text = city is null ? "" : city.Id != store.Current.City?.Id && reading is null
                ? "应用设置后查询所选城市的天气。" : WeatherReading.Summary(city, reading);
        }
        if (ClockPage.Visibility == Visibility.Visible) {
            bool current = CurrentMode.IsChecked == true;
            if (current) gallery.ShowCurrent(now, draft, calendar, reading);
            GalleryNote.Text = !current ? "示例取 2026 年 11 月 26 日，天气使用深圳样例。"
                : city is null ? "使用本机当前时间。天气样式可在「城市与天气」中选择城市。" : "使用本机当前时间和所选城市的数据。";
        }
        string? error = null;
        try {
            ClockFormatter.Validate(draft.Clock);
            var lines = ClockFormatter.Render(now, draft, calendar, reading);
            PreviewTop.Text = lines.Upper;
            PreviewBottom.Text = lines.Lower;
        } catch (FormatException ex) {
            PreviewTop.Text = "—"; PreviewBottom.Text = ""; error = ex.Message;
        }
        if (error is null) {
            try { SettingsStore.ValidateCalendar(Draft(true).Calendar); }
            catch (FormatException ex) { error = ex.Message; }
        }
        ErrorLine.Text = store.ReadError ?? error ?? saveError ?? "";
        ErrorLine.Visibility = ErrorLine.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ApplyButton.IsEnabled = error is null && store.ReadError is null;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try { store.Save(Draft(true)); CancelSearch(); saved(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) {
            saveError = ex.Message;
            Tick(DateTime.Now);
        }
    }
    private void WeatherRouteChanged(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        CancelSearch();
        Cities.ItemsSource = null;
        Cities.Visibility = Visibility.Collapsed;
        SearchStatus.Text = "可重新搜索城市，应用设置后使用所选连接。";
    }
    private void SearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        SearchClick(sender, e);
    }
    private async void SearchClick(object sender,RoutedEventArgs e)
    {
        CancelSearch(); var query=Query.Text.Trim();
        Cities.Visibility=Visibility.Collapsed;
        if(query.Length < 2) { SearchStatus.Text="请输入至少两个字符的城市名。"; return; }
        using var source=new CancellationTokenSource(); search=source; SearchStatus.Text="正在搜索…";
        try {
            var result=await weather.Search(query,source.Token, DirectWeather.IsChecked == true);
            if(source.IsCancellationRequested)return;
            Cities.ItemsSource=result; Cities.Visibility=result.Count>0?Visibility.Visible:Visibility.Collapsed;
            SearchStatus.Text=result.Count==0?"没有找到城市，试试拼音。":"选择城市后点击「应用设置」。";
        } catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) {
            if (!source.IsCancellationRequested) SearchStatus.Text = ex switch {
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => "城市搜索请求过于频繁，请稍后重试。",
                OperationCanceledException or HttpRequestException { StatusCode: null } => DirectWeather.IsChecked == true
                    ? "无法连接城市服务。请检查网络，或关闭直连后重新搜索。" : "无法连接城市服务。请检查系统代理，或勾选「直连天气服务」后重新搜索。",
                _ => "城市服务暂未返回有效结果，请稍后重试。"
            };
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
