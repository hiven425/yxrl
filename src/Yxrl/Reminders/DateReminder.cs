using System.Globalization;
using System.Text.Json.Serialization;

namespace Yxrl;

internal sealed record DateReminder(Guid Id, string Title, DateTimeOffset ScheduledAt, int LeadMinutes,
    DateTimeOffset? DeliveredAt = null)
{
    public const int TitleLimit = 100;
    public const int LeadLimit = 30 * 24 * 60;

    [JsonIgnore] public DateOnly Date => DateOnly.FromDateTime(ScheduledAt.DateTime);
    [JsonIgnore] public DateTimeOffset NotifyAt => ScheduledAt.AddMinutes(-LeadMinutes);

    public static DateReminder Create(Guid id, string title, DateOnly date, TimeOnly time, int leadMinutes, TimeZoneInfo zone)
    {
        if (date < CalendarService.First || date > CalendarService.Last)
            throw new FormatException("请选择月历支持范围内的日期。");
        var localTime = date.ToDateTime(time);
        if (zone.IsInvalidTime(localTime))
            throw new FormatException("本地时区在这一天跳过了该时间，请换一个时间。");
        var utc = TimeZoneInfo.ConvertTimeToUtc(localTime, zone);
        var reminder = new DateReminder(id, title.Trim(), new DateTimeOffset(localTime, localTime - utc), leadMinutes);
        reminder.Validate();
        return reminder;
    }

    public void Validate()
    {
        if (Id == Guid.Empty) throw new FormatException("提醒标识为空。");
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > TitleLimit)
            throw new FormatException($"请输入 1 到 {TitleLimit} 个字符的提醒标题。");
        if (Date < CalendarService.First || Date > CalendarService.Last)
            throw new FormatException("请选择月历支持范围内的日期。");
        if (LeadMinutes is < 0 or > LeadLimit)
            throw new FormatException($"提前量须为 0 到 {LeadLimit.ToString(CultureInfo.InvariantCulture)} 分钟。");
        if (DeliveredAt < NotifyAt) throw new FormatException("提醒的送达时间早于计划弹框时间。");
    }
}
