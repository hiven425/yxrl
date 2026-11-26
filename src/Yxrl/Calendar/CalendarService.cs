using System.Globalization;
using System.Text.Json;
using Lunar;

namespace Yxrl;

internal sealed record FestivalRule(string Id, string Name, string Kind = "solar", int Month = 0, int Day = 0,
    int Nth = 0, int DayOfWeek = 0)
{
    public override string ToString() => Name;
    internal bool Valid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name) && (Kind switch {
        "solar" => Month is >= 1 and <= 12 && Day >= 1 && Day <= DateTime.DaysInMonth(2000, Month),
        "lunar" => Month is >= 1 and <= 12 && Day is >= 1 and <= 30,
        "dynamic" => Month is >= 1 and <= 12 && (Nth is -1 or >= 1 and <= 5) && DayOfWeek is >= 0 and <= 6,
        "eve" => true, _ => false });
    public bool Matches(DateOnly date, Lunar.Lunar lunar) => Kind switch
    {
        "solar" => date.Month == Month && date.Day == Day,
        "lunar" => lunar.Month == Month && lunar.Day == Day,
        "dynamic" => date.Month == Month && (int)date.DayOfWeek == DayOfWeek &&
            (Nth == -1 ? date.AddDays(7).Month != date.Month : (date.Day - 1) / 7 + 1 == Nth),
        "eve" => lunar.Month == 12 && lunar.Day >= 29 && lunar.Next(1).Day == 1,
        _ => false
    };
}
internal sealed record DayInfo(DateOnly Date, Lunar.Lunar Lunar, string Festivals, string Mark)
{
    public string LunarMonth => Lunar.MonthInChinese + "月";
    public string LunarDate => LunarMonth + Lunar.DayInChinese;
    public string Secondary => !string.IsNullOrEmpty(Festivals) ? Festivals.Split('、')[0]
        : !string.IsNullOrEmpty(Lunar.JieQi) ? Lunar.JieQi : Lunar.Day == 1 ? LunarMonth : Lunar.DayInChinese;
    public string Ganzhi => $"{Lunar.YearInGanZhi}年 {Lunar.MonthInGanZhi}月 {Lunar.DayInGanZhi}日";
}
internal sealed class CalendarService
{
    private sealed record Catalog(FestivalRule[] Festivals, DateOnly[][] Breaks, DateOnly[] Workdays);
    private readonly Catalog catalog;
    private readonly Dictionary<DateOnly, DayInfo> days = [];
    private (DateOnly Date, string? Target, string Result)? lastCountdown;
    public FestivalRule[] Rules { get; }
    public static readonly DateOnly First = new(1901, 1, 1), Last = new(2099, 12, 31);
    public CalendarService(SettingsStore settings)
    {
        using var stream = typeof(CalendarService).Assembly.GetManifestResourceStream("Yxrl.Calendar.CalendarData.json")!;
        catalog = JsonSerializer.Deserialize<Catalog>(stream, SettingsStore.Json)!;
        Rules = catalog.Festivals.Concat(settings.CustomRules).Where(x => !settings.Disabled.Contains(x.Id)).ToArray();
    }
    public static string Weekday(DateOnly day) => "周" + "日一二三四五六"[(int)day.DayOfWeek];
    public DayInfo Get(DateOnly day)
    {
        if (days.TryGetValue(day, out var info)) return info;
        var lunar = new Solar(day.Year, day.Month, day.Day).Lunar;
        var names = string.Join("、", Rules.Where(x => x.Matches(day, lunar)).Select(x => x.Name));
        var mark = catalog.Workdays.Contains(day) ? "班" : catalog.Breaks.Any(r => r[0] <= day && day <= r[1]) ? "休" : "";
        if (days.Count > 600) days.Clear();
        return days[day] = new DayInfo(day, lunar, names, mark);
    }
    public string Countdown(DateOnly today, string? target)
    {
        if (lastCountdown is { } cached && cached.Date == today && cached.Target == target) return cached.Result;
        var rule = Rules.FirstOrDefault(x => x.Id == target);
        string result = "选择倒数节日";
        if (rule is not null)
        {
            result = "暂无日期";
            for (int i = 0; i <= 8 * 366; i++) {
                var date = today.AddDays(i);
                if ((rule.Kind is "solar" or "dynamic") && date.Month != rule.Month) continue;
                var lunar = new Solar(date.Year, date.Month, date.Day).Lunar;
                if (!rule.Matches(date, lunar)) continue;
                result = i == 0 ? "今天" + rule.Name : $"距{rule.Name}{i}天";
                break;
            }
        }
        lastCountdown = (today, target, result);
        return result;
    }
    public static DateOnly Clamp(DateOnly date) => date < First ? First : date > Last ? Last : date;
}
