using System.IO;
using System.Text.Json;

namespace Yxrl;

// Owned by the application Dispatcher, so edits and delivery receipts use the same snapshot.
internal sealed class ReminderStore(string directory)
{
    private sealed record ReminderFile(int SchemaVersion, DateReminder[] Entries);
    private DateReminder[] entries = [];

    public string FilePath { get; } = Path.Combine(directory, "reminders.json");
    public string? ReadError { get; private set; }
    public event Action? Edited;

    public void Load()
    {
        try {
            if (!File.Exists(FilePath)) return;
            var saved = JsonSerializer.Deserialize<ReminderFile>(File.ReadAllText(FilePath), SettingsStore.Json);
            if (saved is not { SchemaVersion: 1, Entries: not null })
                throw new FormatException("无法读取该版本的提醒文件。");
            var ids = new HashSet<Guid>();
            foreach (var entry in saved.Entries) {
                if (entry is null || !ids.Add(entry.Id)) throw new FormatException("提醒标识缺失或重复。");
                entry.Validate();
            }
            entries = saved.Entries;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException) {
            ReadError = $"提醒读取失败，已保留原文件。\n{FilePath}\n{ex.Message}";
        }
    }

    public DateReminder[] ForDate(DateOnly date) =>
        entries.Where(reminder => reminder.Date == date).OrderBy(reminder => reminder.ScheduledAt).ToArray();

    public int CountOn(DateOnly date) => entries.Count(reminder => reminder.Date == date);

    public void Save(DateReminder candidate)
    {
        candidate.Validate();
        var edited = entries.FirstOrDefault(reminder => reminder.Id == candidate.Id);
        var receipt = edited is not null && edited.ScheduledAt == candidate.ScheduledAt &&
            edited.LeadMinutes == candidate.LeadMinutes ? edited.DeliveredAt : null;
        var updated = candidate with { DeliveredAt = receipt };
        Commit([.. entries.Where(reminder => reminder.Id != updated.Id), updated]);
        Edited?.Invoke();
    }

    public void Remove(Guid id)
    {
        Commit(entries.Where(reminder => reminder.Id != id).ToArray());
        Edited?.Invoke();
    }

    public DateReminder[] TakeDue(DateTimeOffset now)
    {
        var due = entries.Where(reminder => reminder.DeliveredAt is null && reminder.NotifyAt <= now)
            .OrderBy(reminder => reminder.NotifyAt).ToArray();
        if (due.Length == 0) return due;
        var ids = due.Select(reminder => reminder.Id).ToHashSet();
        Commit(entries.Select(reminder => ids.Contains(reminder.Id)
            ? reminder with { DeliveredAt = now.ToUniversalTime() } : reminder).ToArray());
        return due;
    }

    private void Commit(DateReminder[] next)
    {
        if (ReadError is not null) throw new IOException(ReadError);
        Directory.CreateDirectory(directory);
        SettingsStore.WriteAtomic(FilePath, JsonSerializer.Serialize(new ReminderFile(1, next), SettingsStore.Json));
        entries = next;
    }
}
