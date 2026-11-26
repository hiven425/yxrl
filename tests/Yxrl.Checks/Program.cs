using System.Text.Json.Nodes;
using Yxrl;
using Yxrl.Desktop;
using Yxrl.Views;

if (args.Contains("--taskbar")) {
    var scan = ClockLocator.Find();
    Console.WriteLine($"原生时钟 {scan.Clocks.Length}，第三方时钟 {scan.OtherClock}，任务栏收起 {scan.Retracted}");
    foreach(var a in scan.Clocks)
        Console.WriteLine($"时钟 {a.Bounds}，任务栏 {a.Bar}，DPI {a.Dpi}，命中原任务栏 {Native.OnTaskbar(a.Taskbar,a.Bounds.Center)}");
    return;
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"期望 {expected}，得到 {actual}");
}
TaskbarChecks.Verify();
var directory = Directory.CreateTempSubdirectory("yxrl-check-");
try
{
    ReminderChecks.Verify(directory.FullName);
    if(args.Contains("--weather")) {
        using var service=new WeatherService(directory.FullName);
        using var time=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        bool direct = args.Contains("--direct");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var matches=await service.Search("深圳市", time.Token, direct);
        var city=matches.First(x=>x.Latitude is > 22 and < 23 && x.Longitude is > 113 and < 115);
        Console.WriteLine($"找到{city.Label}，耗时 {timer.Elapsed.TotalSeconds:F1} 秒。");
        timer.Restart();
        await service.Refresh(city, time.Token, direct);
        using var reopenedWeather = new WeatherService(directory.FullName);
        reopenedWeather.Load();
        var data=reopenedWeather.Read(city);
        if(data?.Current is null || data.Air is null) throw new Exception(service.Warning ?? "缺少天气或空气质量");
        Equal(service.Read(city), data);
        Console.WriteLine($"{WeatherReading.Summary(city, data)}\n气温来源 {data.Current.Source}，空气质量来源 {data.Air.Source}。刷新并读回耗时 {timer.Elapsed.TotalSeconds:F1} 秒。");
        if (data.Current.Daylight is { } daylight)
            Console.WriteLine($"地点日出 {daylight.Sunrise:HH:mm}，日落 {daylight.Sunset:HH:mm}。");
        return;
    }
    var store=new SettingsStore(directory.FullName); store.Load();
    var calendar=new CalendarService(store);
    var date=new DateOnly(2026,11,26); var now=date.ToDateTime(new TimeOnly(10,32,1));
    var day=calendar.Get(date);
    Equal("十月十八",day.LunarDate); Equal("感恩节",day.Festivals); Equal("丙午年 己亥月 甲辰日",day.Ganzhi);
    Equal("班",calendar.Get(new(2026,10,10)).Mark); Equal("",calendar.Get(new(2026,10,17)).Mark);
    Equal("距春节72天",calendar.Countdown(date,"spring"));
    Equal("闰六月初一",calendar.Get(new(2025,7,25)).LunarDate);
    var month=new MonthViewModel(calendar); month.Select(date);
    var range=new CalendarOptions(From:new(2026,11,23),To:new(2026,11,29));
    Equal("48",string.Join(",",month.Weeks(range,date).Where(w=>w.Number.HasValue).Select(w=>w.Number)));
    Equal(53,new CalendarOptions(From:new(2027,1,1),To:new(2027,1,1)).RowWeek(new(2026,12,27)));
    Equal(null,range.Week(new(2026,11,30)));
    var pref=store.Current with { Clock=new(Preset:"custom",Top:"hh:ff:ss 星期W JJ",Bottom:"{农历} 第{周数}周") };
    var clock=ClockFormatter.Render(now,pref,calendar,null);
    Equal("10:32:01 星期四 感恩节",clock.Upper); Equal("十月十八 第48周",clock.Lower);
    var gallery = new ClockStyleGallery();
    var examples = gallery.Tiles.Select(tile => tile.Lines).ToArray();
    Equal(9, examples.Length);
    Equal(true, examples.All(item => item.Upper.Length > 0 && item.Lower.Length > 0));
    ClockText Tile(string id) => gallery.Tiles.Single(tile => tile.Id == id).Lines;
    Equal("2026-11-26", Tile("simple").Lower);
    Equal("11月26日 十月十八", Tile("dual").Lower);
    Equal("距春节72天", Tile("countdown").Lower);
    Equal("20～26° 美标AQI 42", Tile("forecast").Lower);
    gallery.ShowCurrent(now.AddDays(1), pref, calendar, null);
    Equal("11月27日 周五", Tile("seconds").Lower);
    Equal("请选择城市", Tile("forecast").Lower);
    gallery.ShowExamples();
    Equal(true, examples.SequenceEqual(gallery.Tiles.Select(tile => tile.Lines)));
    foreach(var style in ClockFormatter.Styles) {
        var lines=ClockFormatter.Render(now,pref with { Clock=pref.Clock with { Preset=style.Id } },calendar,null);
        if(lines.Upper.Length==0 && lines.Lower.Length==0) throw new Exception(style.Id);
    }
    File.WriteAllText(store.FilePath,"{\"schemaVersion\":1,\"general\":{\"trayIcon\":true},\"clock\":{\"futureKey\":1}}");
    store.Load(); store.Save(pref); var reopened=new SettingsStore(directory.FullName); reopened.Load();
    Equal(pref,reopened.Current);
    var doc=JsonNode.Parse(File.ReadAllText(store.FilePath)); Equal(true,(bool?)doc?["general"]?["trayIcon"]);
    Equal(1,(int?)doc?["clock"]?["futureKey"]);
    File.WriteAllText(store.FilePath,"broken"); var broken=new SettingsStore(directory.FullName); broken.Load();
    try { broken.Save(pref); throw new Exception("损坏配置被覆盖"); } catch(IOException) { Equal("broken",File.ReadAllText(store.FilePath)); }
    await WeatherChecks.Run(directory.FullName);
    Console.WriteLine("通过：离线历法、节日与休班、ISO 周边界、九种双行样式与示例切换、范围与配置读回。");
}
finally { directory.Delete(true); }
