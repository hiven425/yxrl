using System.Globalization;
using System.Text;

namespace Yxrl;

internal sealed record ClockStyle(string Id, string Name)
{
    public override string ToString() => Name;
}
internal sealed record ClockText(string Upper, string Lower, string Time);
internal static class ClockFormatter
{
    public static readonly ClockStyle[] Styles = [
        new("simple","公历简约"),new("dual","公农双历"),new("seconds","星期秒钟"),new("countdown","节日倒数"),
        new("festive","节日节气"),new("weather","当前天气"),new("hours","十二时辰"),new("week","周数日历"),
        new("forecast","天气详情"),new("custom","自定义两行") ];
    private static readonly string[] Tokens = ["yyyy","YYYY","ddd","HH","hh","mm","ff","ss","MM","dd","JJ","tt","M","d","W","h"];
    private static readonly HashSet<string> Fields = ["农历","节日","节气","周数","时辰","倒数","城市","天气","温度范围","美标AQI"];
    public static string Time(DateTime now, bool hour12, bool seconds)
    {
        var prefix = hour12 ? now.Hour < 12 ? "上午" : "下午" : "";
        var hour = hour12 ? now.ToString("%h", CultureInfo.InvariantCulture) : now.ToString("HH", CultureInfo.InvariantCulture);
        return prefix + hour + now.ToString(seconds ? ":mm:ss" : ":mm", CultureInfo.InvariantCulture);
    }
    public static void Validate(ClockOptions opt)
    {
        if (!Styles.Any(x => x.Id == opt.Preset)) throw new FormatException("请选择已有的时钟样式。");
        if (opt.Top.Length > 64 || opt.Bottom.Length > 64 || string.IsNullOrWhiteSpace(opt.Top + opt.Bottom))
            throw new FormatException("每行最多 64 个字符，至少填写一行。");
        Format(opt.Top, _ => ""); Format(opt.Bottom, _ => "");
    }
    private static string Format(string input, Func<string,string> value)
    {
        var result = new StringBuilder();
        for (int i = 0; i < input.Length;)
        {
            if (input[i] == '{') {
                var end = input.IndexOf('}', i + 1);
                if (end < 0 || !Fields.Contains(input[(i + 1)..end])) throw new FormatException("请检查花括号中的字段名称。");
                result.Append(value(input[(i + 1)..end])); i = end + 1; continue;
            }
            var token = Tokens.FirstOrDefault(t => input.AsSpan(i).StartsWith(t, StringComparison.Ordinal));
            if (token is not null) { result.Append(value(token)); i += token.Length; continue; }
            if (char.IsAsciiLetter(input[i]) || input[i] == '}') throw new FormatException($"无法识别格式字符「{input[i]}」。");
            result.Append(input[i++]);
        }
        return result.ToString().Trim();
    }
    public static ClockText Render(DateTime now, Preferences settings, CalendarService calendar, WeatherReading? weather)
    {
        var opt = settings.Clock;
        var date = DateOnly.FromDateTime(now); var day = calendar.Get(date);
        var week = CalendarService.Weekday(date); var iso = settings.Calendar.Week(date);
        var dateText = $"{date.Month}月{date.Day}日"; var numeric = $"{date.Year}-{date.Month}-{date.Day}";
        var time = Time(now, settings.Calendar.Hour12, false); var stamp = Time(now, settings.Calendar.Hour12, true);
        var phase = "子丑寅卯辰巳午未申酉戌亥"[(now.Hour + 1) / 2 % 12] + "时";
        var holiday = string.Join(" ", new[] { day.Festivals, day.Lunar.JieQi }.Where(s => !string.IsNullOrEmpty(s)));
        var city = settings.City?.Name ?? "";
        var current = weather?.Current;
        var weatherText = WeatherReading.CurrentText(settings.City, weather);
        var range = current is null ? "" : $"{current.Low:0}～{current.High:0}°";
        var aqi = weather?.Air is { } air ? $"美标AQI {air.Aqi:0}{(air.IsStale ? " 缓存" : "")}" : "";
        var forecastCondition = current?.Condition + (current?.IsStale == true ? " 缓存" : "");
        if (current is null && weather?.Air is not null) forecastCondition = weatherText;
        var forecastAir = current is not null && weather?.Air is null
            ? weather?.AirPending == true ? "美标AQI 查询中" : "美标AQI 暂不可用" : aqi;
        var forecastDetails = string.Join(" ", new[] { range, forecastAir }.Where(x => x.Length > 0));
        var bottom = dateText + " " + week;
        string Value(string t) => t switch {
            "yyyy" or "YYYY" => date.Year.ToString(), "HH" or "hh" => now.ToString("HH"), "mm" or "ff" => now.ToString("mm"),
            "ss" => now.ToString("ss"), "MM" => now.ToString("MM"), "M" => date.Month.ToString(),
            "dd" => now.ToString("dd"), "d" => date.Day.ToString(), "ddd" => week, "W" => week[1..], "JJ" => holiday,
            "h" => now.ToString("%h"), "tt" => now.Hour < 12 ? "上午" : "下午", "农历" => day.LunarDate,
            "节日" => day.Festivals, "节气" => day.Lunar.JieQi, "周数" => iso?.ToString() ?? "", "时辰" => phase,
            "倒数" => calendar.Countdown(date,opt.Target), "城市" => city, "天气" => weatherText, "温度范围" => range,
            "美标AQI" => aqi, _ => throw new FormatException(t) };
        ClockText lines = opt.Preset switch {
            "simple" => new(time + " " + week, numeric, time),
            "dual" => new(time + " " + week, dateText + " " + day.LunarDate, time),
            "seconds" => new(stamp, bottom, stamp),
            "countdown" => new(time + " " + week, calendar.Countdown(date,opt.Target), time),
            "festive" => new((stamp + " " + week + " " + holiday).TrimEnd(), dateText + " " + day.LunarDate, stamp),
            "weather" => new(time + " " + weatherText, bottom, time),
            "hours" => new(Time(now,true,true) + " " + phase, bottom, Time(now,true,true)),
            "week" => new(iso.HasValue ? $"{stamp} 第{iso}周" : stamp, numeric + " " + week, stamp),
            "forecast" => new($"{time} {city} {forecastCondition}".TrimEnd(), forecastDetails.Length > 0 ? forecastDetails : weatherText, time),
            "custom" => new(Format(opt.Top,Value),Format(opt.Bottom,Value), stamp),
            _ => throw new FormatException("未知时钟样式") };
        if (weather?.UsesVisualCrossing == true) {
            var fields = opt.Top + " " + opt.Bottom;
            bool attributed = opt.Preset switch {
                "forecast" => true,
                "weather" => current?.Source == VisualCrossing.Name,
                "custom" => current?.Source == VisualCrossing.Name && (fields.Contains("{天气}") || fields.Contains("{温度范围}"))
                    || weather.Air?.Source == VisualCrossing.Name && fields.Contains("{美标AQI}"),
                _ => false
            };
            if (attributed) return lines with { Lower = (lines.Lower + " " + VisualCrossing.Name).TrimStart() };
        }
        return lines;
    }
}
