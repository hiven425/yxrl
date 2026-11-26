namespace Yxrl.Views;

internal sealed record DayCell(DayInfo Day, bool Outside, bool IsToday, bool IsSelected);
internal sealed record WeekRow(int? Number, DayCell?[] Days);
internal sealed class MonthViewModel(CalendarService calendar)
{
    public DateOnly Selected { get; private set; }
    public DayInfo Detail => calendar.Get(Selected);
    public DateOnly Month => new(Selected.Year,Selected.Month,1);
    public void Select(DateOnly day) => Selected = CalendarService.Clamp(day);
    public void Shift(int months) => Select(Selected.AddMonths(months));
    public IReadOnlyList<WeekRow> Weeks(CalendarOptions opt, DateOnly today)
    {
        var start = Month.AddDays(-((int)Month.DayOfWeek - opt.WeekStart + 7) % 7);
        var rows = new List<WeekRow>(6);
        for (var row = start; row < Month.AddMonths(1); row = row.AddDays(7)) {
            var cells = new DayCell?[7];
            for (int col=0;col<7;col++) {
                var day = row.AddDays(col);
                var outside = day.Month != Month.Month;
                if (day >= CalendarService.First && day <= CalendarService.Last && (!outside || opt.ShowOutside))
                    cells[col] = new(calendar.Get(day),outside,day==today,day==Selected);
            }
            rows.Add(new(opt.RowWeek(row),cells));
        }
        return rows;
    }
}
