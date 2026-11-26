using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Yxrl;

internal static class VisualCrossing
{
    internal const string Name = "Visual Crossing Weather";
    private const string Timeline = "https://weather.visualcrossing.com/VisualCrossingWebServices/rest/services/timeline/";

    internal static async Task<WeatherReading> Fetch(HttpClient client, City city, bool needWeather, bool needAir, CancellationToken token)
    {
        var location = FormattableString.Invariant($"{city.Latitude},{city.Longitude}");
        var include = needWeather ? "current,days" : "current";
        var elements = "datetimeEpoch,tzoffset" + (needWeather ? ",temp,tempmin,tempmax,icon,sunriseEpoch,sunsetEpoch" : "") + (needAir ? ",aqius" : "");
        var url = $"{Timeline}{location}/today?unitGroup=metric&include={include}&elements={elements}";
        if (needWeather) url += "&iconSet=icons2";
        using var doc = JsonDocument.Parse(await client.GetStringAsync(url, token));
        var root = doc.RootElement;
        var now = root.GetProperty("currentConditions");
        var epoch = now.GetProperty("datetimeEpoch").GetInt64();
        var offset = Number(now, "tzoffset") ?? Number(root, "tzoffset");
        if (offset is null or < -14 or > 14 || epoch is < -62135596800 or > 253402300799)
            throw new JsonException("天气来源时间无效。");
        var localTime = DateTimeOffset.FromUnixTimeSeconds(epoch).ToOffset(TimeSpan.FromMinutes(Math.Round(offset.Value * 60)));
        var observed = localTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        var fetched = DateTimeOffset.UtcNow;

        WeatherPart? current = null;
        if (needWeather && Number(now, "temp") is double temp && root.TryGetProperty("days", out var days)
            && days.ValueKind == JsonValueKind.Array && days.GetArrayLength() > 0
            && Number(days[0], "tempmin") is double low && Number(days[0], "tempmax") is double high) {
            string? icon = now.TryGetProperty("icon", out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            bool? isDay = icon?.EndsWith("-day", StringComparison.Ordinal) == true ? true
                : icon?.EndsWith("-night", StringComparison.Ordinal) == true ? false : null;
            var dayOffset = Number(days[0], "tzoffset") ?? offset.Value;
            current = new(temp, low, high, Condition(icon), observed, fetched, Name) {
                Daylight = new(isDay, SolarTime(days[0], "sunriseEpoch", dayOffset), SolarTime(days[0], "sunsetEpoch", dayOffset))
            };
        }
        var air = needAir && Number(now, "aqius") is double aqi && aqi >= 0
            ? new AirPart(aqi, observed, fetched, Name) : null;
        return new(current, air);
    }

    private static DateTimeOffset? SolarTime(JsonElement day, string field, double offset)
    {
        if (offset is < -14 or > 14 || !day.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var epoch)) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(epoch).ToOffset(TimeSpan.FromMinutes(Math.Round(offset * 60))); }
        catch (ArgumentException) { return null; }
    }

    private static double? Number(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number
        && field.TryGetDouble(out var value) && double.IsFinite(value) ? value : null;

    private static string Condition(string? icon) => icon switch {
        "clear-day" or "clear-night" => "晴", "partly-cloudy-day" or "partly-cloudy-night" => "多云",
        "cloudy" => "阴", "rain" => "雨", "showers-day" or "showers-night" => "阵雨",
        "snow" => "雪", "snow-showers-day" or "snow-showers-night" => "阵雪",
        "thunder-rain" or "thunder-showers-day" or "thunder-showers-night" => "雷雨",
        "wind" => "大风", "fog" => "雾", _ => "天气未知"
    };
}
