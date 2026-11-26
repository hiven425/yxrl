using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Yxrl;

internal sealed record WeatherPart(double Temp, double Low, double High, string Condition, string ObservedAt, DateTimeOffset FetchedAt);
internal sealed record AirPart(double Aqi, string ObservedAt, DateTimeOffset FetchedAt);
internal sealed record WeatherReading(WeatherPart? Current = null, AirPart? Air = null);

internal sealed class WeatherService(string directory) : IDisposable
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly string path = Path.Combine(directory, "weather-cache.json");
    private Dictionary<long, WeatherReading> cache = [];
    public string? Warning { get; private set; }
    public WeatherReading? Read(City? city) => city is null ? null : cache.GetValueOrDefault(city.Id);
    public void Load()
    {
        try { if (File.Exists(path)) cache = JsonSerializer.Deserialize<Dictionary<long, WeatherReading>>(File.ReadAllText(path), SettingsStore.Json) ?? []; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { Warning = "天气缓存读取失败，联网后将重新获取。"; }
    }
    public async Task<List<City>> Search(string query, CancellationToken token)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=zh&name=" + Uri.EscapeDataString(query.Trim());
        using var doc = await GetJson(url,token);
        var result = new List<City>();
        if (doc.RootElement.TryGetProperty("results", out var hits))
            foreach (var hit in hits.EnumerateArray()) {
                string Field(string key) => hit.TryGetProperty(key,out var val) ? val.GetString() ?? "" : "";
                result.Add(new City(hit.GetProperty("id").GetInt64(), Field("name"), hit.GetProperty("latitude").GetDouble(),
                    hit.GetProperty("longitude").GetDouble(), string.Join(" ",new[]{Field("admin1"),Field("country")}.Where(x=>x.Length>0)),Field("timezone")));
            }
        return result;
    }
    private static string Condition(int code) => code switch {
        0 => "晴", 1 => "大部晴朗", 2 => "多云", 3 => "阴", 45 or 48 => "雾", >= 51 and <= 57 => "毛毛雨",
        >= 61 and <= 67 => "雨", >= 71 and <= 77 => "雪", >= 80 and <= 82 => "阵雨", 85 or 86 => "阵雪",
        >= 95 and <= 99 => "雷雨", _ => "天气未知" };
    private async Task<WeatherPart> Forecast(string query, CancellationToken token)
    {
        using var doc = await GetJson("https://api.open-meteo.com/v1/forecast?" + query +
            "&current=temperature_2m,weather_code&daily=temperature_2m_min,temperature_2m_max&forecast_days=1", token);
        var now = doc.RootElement.GetProperty("current"); var day = doc.RootElement.GetProperty("daily");
        return new(now.GetProperty("temperature_2m").GetDouble(), day.GetProperty("temperature_2m_min")[0].GetDouble(),
            day.GetProperty("temperature_2m_max")[0].GetDouble(), Condition(now.GetProperty("weather_code").GetInt32()),
            now.GetProperty("time").GetString()!, DateTimeOffset.UtcNow);
    }
    private async Task<AirPart> Air(string query, CancellationToken token)
    {
        using var doc = await GetJson("https://air-quality-api.open-meteo.com/v1/air-quality?" + query + "&current=us_aqi",token);
        var now = doc.RootElement.GetProperty("current");
        return new(now.GetProperty("us_aqi").GetDouble(), now.GetProperty("time").GetString()!, DateTimeOffset.UtcNow);
    }
    private async Task<JsonDocument> GetJson(string url,CancellationToken token)
    {
        try { return JsonDocument.Parse(await client.GetStringAsync(url,token)); }
        catch(HttpRequestException ex) when (!token.IsCancellationRequested &&
            (ex.StatusCode is null or System.Net.HttpStatusCode.RequestTimeout || (int)ex.StatusCode >= 500)) { }
        catch(TaskCanceledException) when (!token.IsCancellationRequested) { }
        await Task.Delay(300,token);
        return JsonDocument.Parse(await client.GetStringAsync(url,token));
    }
    private static async Task<(T? Part,bool Failed)> RefreshPart<T>(T? previous, DateTimeOffset? fetched,TimeSpan lifetime,
        Func<Task<T>> fetch, CancellationToken token) where T : class
    {
        if (fetched.HasValue && DateTimeOffset.UtcNow - fetched < lifetime) return (previous,false);
        try { return (await fetch(),false); }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException
            or InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException)
        { return (previous,true); }
    }
    public async Task Refresh(City city, CancellationToken token)
    {
        var previous = Read(city);
        var coord = FormattableString.Invariant($"latitude={city.Latitude}&longitude={city.Longitude}&timezone=auto");
        var wt = RefreshPart(previous?.Current, previous?.Current?.FetchedAt, TimeSpan.FromMinutes(30), () => Forecast(coord,token), token);
        var at = RefreshPart(previous?.Air, previous?.Air?.FetchedAt, TimeSpan.FromHours(1), () => Air(coord,token), token);
        await Task.WhenAll(wt, at);
        token.ThrowIfCancellationRequested();
        var (current,wFailed) = await wt; var (air,aFailed) = await at;
        Warning = wFailed || aFailed ? "天气或空气质量暂未更新，已有数据保留采集时间。" : null;
        var reading = new WeatherReading(current,air);
        if (reading == previous || current is null && air is null) return;
        cache[city.Id] = reading;
        try { Directory.CreateDirectory(directory); SettingsStore.WriteAtomic(path,JsonSerializer.Serialize(cache,SettingsStore.Json)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warning = "本次天气已获取，缓存写入失败。"; }
    }
    public void Dispose() => client.Dispose();
}
