using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yxrl;

internal sealed record WeatherDaylight(bool? IsDay, DateTimeOffset? Sunrise, DateTimeOffset? Sunset);

internal sealed record WeatherPart(double Temp, double Low, double High, string Condition, string ObservedAt,
    DateTimeOffset FetchedAt, string Source = "Open-Meteo")
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WeatherDaylight? Daylight { get; init; }

    internal bool IsStale => DateTimeOffset.UtcNow - FetchedAt >= TimeSpan.FromMinutes(30);
    internal bool IsValid => double.IsFinite(Temp) && double.IsFinite(Low) && double.IsFinite(High) && Low <= High
        && !string.IsNullOrWhiteSpace(Condition) && !string.IsNullOrWhiteSpace(ObservedAt) && FetchedAt != default;
}

internal sealed record AirPart(double Aqi, string ObservedAt, DateTimeOffset FetchedAt, string Source = "Open-Meteo")
{
    internal bool IsStale => DateTimeOffset.UtcNow - FetchedAt >= TimeSpan.FromHours(1);
    internal bool IsValid => double.IsFinite(Aqi) && Aqi >= 0 && !string.IsNullOrWhiteSpace(ObservedAt) && FetchedAt != default;
}

internal sealed record WeatherReading(WeatherPart? Current = null, AirPart? Air = null)
{
    internal static readonly WeatherReading Unavailable = new();
    internal bool WeatherPending { get; init; }
    internal bool AirPending { get; init; }
    internal bool UsesVisualCrossing => Current?.Source == VisualCrossing.Name || Air?.Source == VisualCrossing.Name;
    internal bool UsesOpenMeteo => Current?.Source == "Open-Meteo" || Air?.Source == "Open-Meteo";
    internal WeatherReading ValidParts() => new(Current is { IsValid: true } ? Current : null, Air is { IsValid: true } ? Air : null);

    internal static string CurrentText(City? city, WeatherReading? reading) => reading?.Current is { } part
        ? $"{part.Condition} {part.Temp:0}°{(part.IsStale ? " 缓存" : "")}"
        : city is null ? "请选择城市" : reading is null or { WeatherPending: true } ? "天气待更新" : "天气暂不可用";

    internal static string Summary(City? city, WeatherReading? reading)
    {
        if (city is null) return "在设置的「城市与天气」中选择城市。";
        var current = $"{city.Name}  {CurrentText(city, reading)}";
        if (reading is null) return current;
        if (reading.Current is { } w) current += $" · {w.ObservedAt.Replace('T', ' ')}";
        var air = reading.Air is { } a
            ? $"美标 AQI {a.Aqi:0}{(a.IsStale ? " 缓存" : "")} · {a.ObservedAt.Replace('T', ' ')}"
            : reading.AirPending ? "美标 AQI 查询中" : "美标 AQI 暂不可用";
        return current + "\n" + air;
    }
}

internal sealed class WeatherService(string directory, HttpMessageHandler? systemHandler = null,
    HttpMessageHandler? directHandler = null) : IDisposable
{
    private static HttpClient CreateClient(bool useProxy, HttpMessageHandler? handler) =>
        new(handler ?? new HttpClientHandler { UseProxy = useProxy, AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(8) };

    private readonly HttpClient system = CreateClient(true, systemHandler);
    private readonly HttpClient direct = CreateClient(false, directHandler);
    private readonly string path = Path.Combine(directory, "weather-cache.json");
    private Dictionary<long, WeatherReading> cache = [];
    public string? Warning { get; private set; }
    public WeatherReading? Read(City? city) => city is null ? null : cache.GetValueOrDefault(city.Id);
    public void Load()
    {
        try {
            if (!File.Exists(path)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<long, WeatherReading?>>(File.ReadAllText(path), SettingsStore.Json) ?? [];
            cache = [];
            foreach (var (id, entry) in saved) {
                var valid = entry?.ValidParts();
                if (valid is not null && valid != WeatherReading.Unavailable) cache[id] = valid;
                if (entry is null || valid != entry) Warning = "部分天气缓存无效，联网后将重新获取。";
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { Warning = "天气缓存读取失败，联网后将重新获取。"; }
    }
    public async Task<List<City>> Search(string query, CancellationToken token, bool directConnection = false)
    {
        var client = directConnection ? direct : system;
        string name = query.Trim();
        var cities = await SearchName(client, name, token);
        if (cities.Count == 0 && name.Length > 2 && name.EndsWith('市'))
            cities = await SearchName(client, name[..^1], token);
        return cities;
    }

    private static async Task<List<City>> SearchName(HttpClient client, string name, CancellationToken token)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=zh&name=" + Uri.EscapeDataString(name);
        string json;
        try { json = await client.GetStringAsync(url, token); }
        catch (Exception ex) when (!token.IsCancellationRequested && IsTransient(ex)) {
            await Task.Delay(300, token);
            json = await client.GetStringAsync(url, token);
        }
        using var doc = JsonDocument.Parse(json);
        var result = new List<City>();
        if (doc.RootElement.TryGetProperty("results", out var hits))
            foreach (var hit in hits.EnumerateArray()) {
                string Field(string key) => hit.TryGetProperty(key,out var val) ? val.GetString() ?? "" : "";
                var city = new City(hit.GetProperty("id").GetInt64(), Field("name"), hit.GetProperty("latitude").GetDouble(),
                    hit.GetProperty("longitude").GetDouble(), string.Join(" ",new[]{Field("admin1"),Field("country")}.Where(x=>x.Length>0)),Field("timezone"));
                if (!city.HasValidLocation) throw new JsonException("城市位置无效。");
                result.Add(city);
            }
        return result;
    }
    private static string Condition(int code) => code switch {
        0 => "晴", 1 => "大部晴朗", 2 => "多云", 3 => "阴", 45 or 48 => "雾", >= 51 and <= 57 => "毛毛雨",
        >= 61 and <= 67 => "雨", >= 71 and <= 77 => "雪", >= 80 and <= 82 => "阵雨", 85 or 86 => "阵雪",
        >= 95 and <= 99 => "雷雨", _ => "天气未知" };
    private static async Task<WeatherReading> Forecast(HttpClient client, string query, CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("https://api.open-meteo.com/v1/forecast?" + query +
            "&current=temperature_2m,weather_code,is_day&daily=temperature_2m_min,temperature_2m_max,sunrise,sunset&forecast_days=1", token));
        var root = doc.RootElement;
        var now = root.GetProperty("current");
        var day = root.GetProperty("daily");
        bool? isDay = now.TryGetProperty("is_day", out var daylight) && daylight.ValueKind == JsonValueKind.Number
            && daylight.TryGetInt32(out var flag) && flag is 0 or 1 ? flag == 1 : null;
        TimeSpan? offset = root.TryGetProperty("utc_offset_seconds", out var utcOffset) && utcOffset.ValueKind == JsonValueKind.Number
            && utcOffset.TryGetInt32(out var seconds) && seconds is >= -50400 and <= 50400 && seconds % 60 == 0
            ? TimeSpan.FromSeconds(seconds) : null;
        return new(Current: new WeatherPart(now.GetProperty("temperature_2m").GetDouble(), day.GetProperty("temperature_2m_min")[0].GetDouble(),
            day.GetProperty("temperature_2m_max")[0].GetDouble(), Condition(now.GetProperty("weather_code").GetInt32()),
            ObservationTime(now), DateTimeOffset.UtcNow) {
            Daylight = new(isDay, SolarTime(day, "sunrise", offset), SolarTime(day, "sunset", offset))
        });
    }

    private static DateTimeOffset? SolarTime(JsonElement day, string field, TimeSpan? offset)
    {
        if (offset is not { } zone || !day.TryGetProperty(field, out var times) || times.ValueKind != JsonValueKind.Array
            || times.GetArrayLength() == 0 || times[0].ValueKind != JsonValueKind.String
            || !DateTime.TryParseExact(times[0].GetString(), "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local)) return null;
        try { return new DateTimeOffset(local, zone); }
        catch (ArgumentException) { return null; }
    }

    private static async Task<WeatherReading> Air(HttpClient client, string query, CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("https://air-quality-api.open-meteo.com/v1/air-quality?" + query + "&current=us_aqi", token));
        var now = doc.RootElement.GetProperty("current");
        return new(Air: new(now.GetProperty("us_aqi").GetDouble(), ObservationTime(now), DateTimeOffset.UtcNow));
    }

    private static string ObservationTime(JsonElement now) =>
        now.GetProperty("time").GetString() is { Length: > 0 } time ? time : throw new JsonException("缺少天气数据时间。");

    private static bool IsTransient(Exception ex) => ex is TaskCanceledException ||
        ex is HttpRequestException http && (http.StatusCode is null or HttpStatusCode.RequestTimeout || (int)http.StatusCode >= 500);

    private static async Task<WeatherReading?> TryFetch(string source, Func<Task<WeatherReading>> fetch, CancellationToken token)
    {
        try { return (await fetch()).ValidParts(); }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException
            or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException) {
            Trace.TraceWarning("{0} 请求失败 ({1})。", source,
                ex is HttpRequestException { StatusCode: { } status } ? status.ToString() : ex.GetType().Name);
            return null;
        }
    }
    private static async Task<WeatherReading?> WaitForPrimary(CancellationToken token)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), token);
        return null;
    }

    public async Task Refresh(City city, CancellationToken token, bool directConnection = false)
    {
        token.ThrowIfCancellationRequested();
        var previous = cache.GetValueOrDefault(city.Id);
        bool needWeather = previous?.Current is not { IsStale: false, Daylight: not null };
        bool needAir = previous?.Air is not { IsStale: false };
        if (!needWeather && !needAir) return;
        var client = directConnection ? direct : system;
        var coord = FormattableString.Invariant($"latitude={city.Latitude}&longitude={city.Longitude}&timezone=auto");
        using var requests = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancel = requests.Token;
        var reading = previous ?? WeatherReading.Unavailable;
        cache[city.Id] = reading with { WeatherPending = needWeather, AirPending = needAir };
        var slow = WaitForPrimary(cancel);
        var pending = new List<Task<WeatherReading?>>();
        if (needWeather) pending.Add(TryFetch("Open-Meteo 天气", () => Forecast(client, coord, cancel), cancel));
        if (needAir) pending.Add(TryFetch("Open-Meteo 空气质量", () => Air(client, coord, cancel), cancel));
        pending.Add(slow);
        bool backupStarted = false;
        try {
            while ((needWeather || needAir) && pending.Count > 0) {
                var done = await Task.WhenAny(pending);
                pending.Remove(done);
                token.ThrowIfCancellationRequested();
                var update = await done;
                var current = needWeather ? update?.Current : null;
                var air = needAir ? update?.Air : null;
                if (current is not null || air is not null) {
                    reading = new(current ?? reading.Current, air ?? reading.Air);
                    needWeather &= current is null;
                    needAir &= air is null;
                    cache[city.Id] = reading with { WeatherPending = needWeather, AirPending = needAir };
                    WriteCache();
                    continue;
                }
                if (!backupStarted) {
                    pending.Remove(slow);
                    backupStarted = true;
                    pending.Add(TryFetch(VisualCrossing.Name, () => VisualCrossing.Fetch(client, city, needWeather, needAir, cancel), cancel));
                }
            }
        } finally {
            requests.Cancel();
            if (token.IsCancellationRequested && previous is null && reading == WeatherReading.Unavailable) cache.Remove(city.Id);
            else cache[city.Id] = reading;
            try { await Task.WhenAll(pending); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        }
    }

    private void WriteCache()
    {
        var saved = cache.Where(entry => entry.Value.Current is not null || entry.Value.Air is not null).ToDictionary();
        try { Directory.CreateDirectory(directory); SettingsStore.WriteAtomic(path, JsonSerializer.Serialize(saved, SettingsStore.Json)); Warning = null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warning = "本次天气已获取，缓存写入失败。"; }
    }
    public void Dispose() { system.Dispose(); direct.Dispose(); }
}
