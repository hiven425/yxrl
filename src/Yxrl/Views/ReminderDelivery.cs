using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

namespace Yxrl.Views;

// Its timer runs even while every calendar/settings window is hidden.
internal sealed class ReminderDelivery : IDisposable
{
    private readonly ReminderStore store;
    private readonly Func<nint> taskbar;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ReminderPopup? popup;
    private DateTimeOffset retryAt;
    private int failures;

    public ReminderDelivery(ReminderStore store, Func<nint> taskbar)
    {
        this.store = store;
        this.taskbar = taskbar;
        timer.Tick += Tick;
        store.Edited += ResetRetry;
    }

    public void Start()
    {
        timer.Start();
        Tick(this, EventArgs.Empty);
    }

    private void ResetRetry()
    {
        retryAt = DateTimeOffset.MinValue;
        failures = 0;
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (store.ReadError is not null || failures >= 3 || now < retryAt) return;
        DateReminder[] due;
        try {
            due = store.TakeDue(now);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            Trace.TraceError($"提醒送达记录写入失败：{ex.Message}");
            retryAt = now.AddSeconds(10);
            if (++failures == 3)
                Show([new("提醒暂时无法弹出", "保存送达记录失败。请确认提醒文件可写，然后重开 YXRL。")]);
            return;
        }
        ResetRetry();
        if (due.Length == 0) return;
        Show(due.Select(reminder => FormatNotice(reminder, now)));
    }

    private void Show(IEnumerable<ReminderNotice> notices)
    {
        if (popup is null) {
            popup = new();
            popup.Closed += (_, _) => popup = null;
        }
        popup.Append(notices, taskbar());
    }

    private static ReminderNotice FormatNotice(DateReminder reminder, DateTimeOffset now)
    {
        var lead = reminder.LeadMinutes == 0 ? "到点提醒" : $"提前 {reminder.LeadMinutes} 分钟";
        var details = $"{reminder.ScheduledAt:yyyy-MM-dd HH:mm}  ·  {lead}";
        if (now - reminder.NotifyAt >= TimeSpan.FromMinutes(1))
            details += $"\n补发 · 原定 {reminder.NotifyAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        return new(reminder.Title, details);
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= Tick;
        store.Edited -= ResetRetry;
        popup?.Close();
    }
}
