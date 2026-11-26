namespace Yxrl.Views;

internal enum WeatherScene { Unknown, Sun, Sunrise, Sunset, Night, PartlyCloudy, CloudyNight, Cloud, Rain, Snow, Thunder, Fog, Wind }

internal static class WeatherAppearance
{
    private static readonly TimeSpan Twilight = TimeSpan.FromMinutes(30);

    internal static WeatherScene At(WeatherPart? current, DateTimeOffset now)
    {
        if (current is null) return WeatherScene.Unknown;
        return current.Condition switch {
            "晴" or "大部晴朗" or "多云" => Sky(current, now),
            "阴" => WeatherScene.Cloud,
            "毛毛雨" or "雨" or "阵雨" => WeatherScene.Rain,
            "雪" or "阵雪" => WeatherScene.Snow,
            "雷雨" => WeatherScene.Thunder,
            "雾" => WeatherScene.Fog,
            "大风" => WeatherScene.Wind,
            _ => WeatherScene.Unknown
        };
    }

    private static WeatherScene Sky(WeatherPart current, DateTimeOffset now)
    {
        var daylight = current.Daylight;
        var sunrise = daylight?.Sunrise;
        var sunset = daylight?.Sunset;
        var sinceRise = sunrise is { } rise ? (now - rise).Duration() : TimeSpan.MaxValue;
        var sinceSet = sunset is { } set ? (now - set).Duration() : TimeSpan.MaxValue;
        if (sinceRise <= Twilight || sinceSet <= Twilight)
            return sinceRise <= sinceSet ? WeatherScene.Sunrise : WeatherScene.Sunset;

        bool night = daylight?.IsDay == false;
        if (sunrise is { } start && sunset is { } end && end > start
            && start.Date == end.Date && start.Date == now.ToOffset(start.Offset).Date)
            night = now < start || now >= end;

        if (current.Condition == "多云") return night ? WeatherScene.CloudyNight : WeatherScene.PartlyCloudy;
        return night ? WeatherScene.Night : WeatherScene.Sun;
    }

    internal static string Label(WeatherScene scene, WeatherPart? current) => scene switch {
        WeatherScene.Sunrise => "日出",
        WeatherScene.Sunset => "日落",
        WeatherScene.Night => "晴夜",
        WeatherScene.CloudyNight => "夜间多云",
        _ => current?.Condition ?? "暂无天气"
    };
}
