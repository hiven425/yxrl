using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Yxrl;
using Yxrl.Views;

internal static class WeatherChecks
{
    private static readonly City Shenzhen = new(1795565, "深圳", 22.55, 114.07);
    private const string CityResult = """{"results":[{"id":1795565,"name":"深圳","latitude":22.55,"longitude":114.07}]}""";
    private const string OutsideAir = """{"current":{"us_aqi":0,"time":"2026-11-26T14:00"}}""";
    private const string Timeline = """
        {"tzoffset":8,"currentConditions":{"datetimeEpoch":1795673940,"temp":19.4,"icon":"partly-cloudy-day","aqius":120},
         "days":[{"tempmin":16,"tempmax":19.7,"sunriseEpoch":1795646580,"sunsetEpoch":1795685940}]}
        """;

    private sealed class Handler(Func<Uri, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request.RequestUri!, token);
    }

    private static Task<HttpResponseMessage> Reply(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static async Task Run(string root)
    {
        var directory = Path.Combine(root, "weather");
        await SearchAndPreferences(directory);
        await SlowForecast(directory);
        await PartialAndOffline(directory);
        await DaylightScenes(Path.Combine(root, "daylight"));
        await Cancel(Path.Combine(root, "cancelled"));
        Console.WriteLine("通过：城市搜索与连接选择、慢请求并发、分项显示和备用源、日照与天气图标、离线缓存与取消。");
    }

    private static async Task SearchAndPreferences(string directory)
    {
        var settings = new SettingsStore(directory);
        settings.Load();
        Check(!settings.Current.DirectWeatherConnection, "默认连接应使用系统代理");
        settings.Save(settings.Current with { City = Shenzhen, DirectWeatherConnection = true });
        var reopened = new SettingsStore(directory);
        reopened.Load();
        Check(reopened.Current == settings.Current, "天气连接设置未能读回");

        var queries = new List<string>();
        using var service = new WeatherService(directory, new Handler((_, _) => Reply("{}", HttpStatusCode.Forbidden)),
            new Handler((uri, _) => {
                string query = Uri.UnescapeDataString(uri.Query);
                queries.Add(query);
                return Reply(query.EndsWith("深圳") ? CityResult : "{}");
            }));
        var matches = await service.Search(" 深圳市 ", default, true);
        Check(matches.Single().Id == Shenzhen.Id && queries.Count == 2 && queries[0].EndsWith("深圳市"), "城市全名与简称查询错误");
        try { await service.Search("深圳市", default); throw new Exception("网络故障被当成了空结果"); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden) { }
    }

    private static async Task SlowForecast(string directory)
    {
        var backupStarted = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBackup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var service = new WeatherService(directory, new Handler(async (uri, token) => {
            Interlocked.Increment(ref calls);
            switch (uri.Host) {
                case "api.open-meteo.com":
                    await Task.Delay(Timeout.Infinite, token);
                    throw new Exception("主天气请求应在备用结果到达后取消");
                case "air-quality-api.open-meteo.com": return await Reply(OutsideAir);
                case "weather.visualcrossing.com":
                    backupStarted.SetResult(uri);
                    await releaseBackup.Task.WaitAsync(token);
                    return await Reply(Timeline);
                default: throw new Exception(uri.Host);
            }
        }));
        var refresh = service.Refresh(Shenzhen, default);
        var backupUrl = await backupStarted.Task.WaitAsync(TimeSpan.FromSeconds(6));
        Check(!backupUrl.Query.Contains("aqius"), "只应补查缺少的天气");
        Check(backupUrl.Query.Contains("sunriseEpoch,sunsetEpoch") && backupUrl.Query.Contains("iconSet=icons2"), "备用源缺少日照或天气图标字段");
        var partial = service.Read(Shenzhen);
        Check(partial is { Air.Aqi: 0, WeatherPending: true } && WeatherReading.Summary(Shenzhen, partial).Contains("美标 AQI 0"),
            "空气质量已到达时应立即显示，包括有效零值");
        releaseBackup.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(3));
        var reading = service.Read(Shenzhen);
        Check(reading is { Current: { Temp: 19.4, Low: 16, High: 19.7, Condition: "多云", ObservedAt: "2026-11-26T14:19", Source: VisualCrossing.Name },
            Air: { Aqi: 0, Source: "Open-Meteo" }, WeatherPending: false }, "备用天气或主源 AQI 被覆盖");
        Check(reading?.Current?.Daylight?.Sunrise == new DateTimeOffset(2026, 11, 26, 6, 43, 0, TimeSpan.FromHours(8))
            && reading.Current.Daylight.Sunset?.Offset == TimeSpan.FromHours(8), "备用源日照时刻按错时区");
        var clock = ClockFormatter.Render(DateTime.Now, new(new(Preset: "forecast"), new(), Shenzhen), new CalendarService(), reading);
        Check(!clock.Lower.Contains(VisualCrossing.Name) && clock.Lower.Contains("16～20°") && clock.Lower.Contains("美标AQI 0"),
            "任务栏天气详情应显示数据且不附带来源名称");
        await service.Refresh(Shenzhen, default);
        Check(calls == 3, "新鲜缓存不应再次联网");
        using var disk = new WeatherService(directory);
        disk.Load();
        Check(disk.Read(Shenzhen) == reading, "分项数据、地点时间和来源未能读回");
    }

    private static async Task PartialAndOffline(string directory)
    {
        var path = Path.Combine(directory, "weather-cache.json");
        var doc = JsonNode.Parse(File.ReadAllText(path))!;
        var old = doc[Shenzhen.Id.ToString()]!;
        old["current"]!["fetchedAt"] = "2026-01-01T00:00:00Z";
        old["air"]!["fetchedAt"] = "2026-01-01T00:00:00Z";
        old["current"]!.AsObject().Remove("source");
        old["current"]!.AsObject().Remove("daylight");
        File.WriteAllText(path, doc.ToJsonString());
        int backups = 0;
        using var service = new WeatherService(directory, new Handler((uri, _) => {
            if (uri.Host != "weather.visualcrossing.com") return Reply("{}", HttpStatusCode.ServiceUnavailable);
            return ++backups == 1 ? Reply("""{"tzoffset":8,"currentConditions":{"datetimeEpoch":1795673940,"aqius":0}}""")
                : Reply("{malformed");
        }));
        service.Load();
        var before = service.Read(Shenzhen);
        await service.Refresh(Shenzhen, default);
        var mixed = service.Read(Shenzhen);
        Check(backups == 1 && mixed?.Current == before?.Current && mixed is
            { Current: { IsStale: true, Source: "Open-Meteo" }, Air: { Aqi: 0, Source: VisualCrossing.Name, IsStale: false } },
            "两项失败应共用一次备用请求，并保留旧格式缓存的原时间");
        Check(WeatherReading.Summary(Shenzhen, mixed).Contains("缓存"), "旧天气缺少缓存提示");

        var other = Shenzhen with { Id = 2, Name = "测试城市" };
        Check(service.Read(other) is null, "切换城市时读取了别处的数据");
        await service.Refresh(other, default);
        Check(WeatherReading.CurrentText(other, service.Read(other)) == "天气暂不可用", "两路失败后仍在显示等待");
        Check(!JsonNode.Parse(File.ReadAllText(path))!.AsObject().ContainsKey("2"), "失败结果不应写成天气缓存");
    }

    private static async Task DaylightScenes(string directory)
    {
        const string forecast = """
            {"utc_offset_seconds":28800,"current":{"temperature_2m":18,"weather_code":0,"is_day":1,"time":"2026-11-26T14:00"},
             "daily":{"temperature_2m_min":[14],"temperature_2m_max":[21],"sunrise":["2026-11-26T06:43"],"sunset":["2026-11-26T17:39"]}}
            """;
        Task<HttpResponseMessage> Send(Uri uri, CancellationToken token) {
            if (uri.Host == "air-quality-api.open-meteo.com") return Reply(OutsideAir);
            Check(uri.Query.Contains("weather_code,is_day") && uri.Query.Contains("sunrise,sunset"), "主源缺少日照字段");
            return Reply(forecast);
        }
        using var service = new WeatherService(directory, new Handler(Send));
        await service.Refresh(Shenzhen, default);
        var weather = service.Read(Shenzhen)?.Current ?? throw new InvalidOperationException("缺少天气");
        var local = TimeSpan.FromHours(8);
        DateTimeOffset At(int hour, int minute) => new(2026, 11, 26, hour, minute, 0, local);
        Check(weather.Daylight is { IsDay: true } && weather.Daylight.Sunrise?.Offset == local, "没有读回地点日照数据");
        Check(WeatherAppearance.At(weather, At(6, 25).ToUniversalTime()) == WeatherScene.Sunrise, "日出场景未按地点时刻触发");
        Check(WeatherAppearance.At(weather, At(17, 49)) == WeatherScene.Sunset, "日落场景错误");
        Check(WeatherAppearance.At(weather, At(14, 0)) == WeatherScene.Sun, "晴天场景错误");
        Check(WeatherAppearance.At(weather, At(22, 0)) == WeatherScene.Night, "缓存的白天值覆盖了当日的日落");
        Check(WeatherAppearance.At(weather with { Condition = "雨" }, At(6, 43)) == WeatherScene.Rain, "日出覆盖了降雨");
        Check(WeatherAppearance.At(weather with { Condition = "雷雨" }, At(17, 39)) == WeatherScene.Thunder, "日落覆盖了雷雨");
        Check(WeatherAppearance.At(weather with { Daylight = null }, At(17, 39)) == WeatherScene.Sun, "旧缓存生成了虚构的日落");
        using var disk = new WeatherService(directory, new Handler(Send));
        disk.Load();
        Check(disk.Read(Shenzhen)?.Current == weather, "日照数据未能从缓存读回");
        var path = Path.Combine(directory, "weather-cache.json");
        var legacy = JsonNode.Parse(File.ReadAllText(path))!;
        legacy[Shenzhen.Id.ToString()]!["current"]!.AsObject().Remove("daylight");
        File.WriteAllText(path, legacy.ToJsonString());
        disk.Load();
        Check(disk.Read(Shenzhen)?.Current is { Daylight: null, IsStale: false }, "旧格式天气无法读取");
        await disk.Refresh(Shenzhen, default);
        Check(disk.Read(Shenzhen)?.Current?.Daylight == weather.Daylight, "未补齐旧版缓存的日照数据");
    }

    private static async Task Cancel(string directory)
    {
        int calls = 0;
        using var service = new WeatherService(directory, new Handler(async (_, token) => {
            Interlocked.Increment(ref calls);
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("请求应被取消");
        }));
        using var cancel = new CancellationTokenSource();
        var refresh = service.Refresh(Shenzhen, cancel.Token);
        Check(calls == 2, "天气与空气质量应当同时发起");
        cancel.Cancel();
        try { await refresh; throw new Exception("取消操作未传播"); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        Check(calls == 2 && service.Read(Shenzhen) is null, "取消后启动了备用请求或留下了错误状态");
    }
}
