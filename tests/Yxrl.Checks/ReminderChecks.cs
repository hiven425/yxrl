using Yxrl;

internal static class ReminderChecks
{
    public static void Verify(string directory)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("验证用 +08", TimeSpan.FromHours(8), "验证用 +08", "验证用 +08");
        var date = new DateOnly(2026, 11, 26);
        var store = new ReminderStore(Path.Combine(directory, "reminder-test"));
        var first = DateReminder.Create(Guid.NewGuid(), "周四例会", date, new TimeOnly(16, 40), 30, zone);
        var second = DateReminder.Create(Guid.NewGuid(), "晚间提醒", date, new TimeOnly(22, 0), 0, zone);
        var expected = Utc(26, 8, 10);
        Equal(expected, first.NotifyAt.ToUniversalTime());
        var previousDay = DateReminder.Create(Guid.NewGuid(), "跨日提醒", date, new TimeOnly(0, 20), 30, zone);
        Equal(new DateOnly(2026, 11, 25), DateOnly.FromDateTime(previousDay.NotifyAt.DateTime));
        Throws<FormatException>(() => DateReminder.Create(Guid.NewGuid(), " ", date, new TimeOnly(9, 0), 0, zone));
        Throws<FormatException>(() => DateReminder.Create(Guid.NewGuid(), "超出提前量", date, new TimeOnly(9, 0),
            DateReminder.LeadLimit + 1, zone));
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        Throws<FormatException>(() => DateReminder.Create(Guid.NewGuid(), "跳过的时刻", new(2026, 3, 8),
            new(2, 30), 0, pacific));

        store.Load();
        store.Save(first);
        store.Save(second);
        Equal(0, store.TakeDue(expected.AddTicks(-1)).Length);
        Equal(first, store.TakeDue(expected).Single());
        Equal(0, store.TakeDue(expected.AddMinutes(1)).Length);
        var reopened = new ReminderStore(Path.GetDirectoryName(store.FilePath)!);
        reopened.Load();
        Equal(null, reopened.ReadError);
        Equal(expected, reopened.ForDate(date).First().DeliveredAt);
        Equal(0, reopened.TakeDue(expected).Length);
        reopened.Save(first with { Title = "更新标题" });
        Equal(expected, reopened.ForDate(date).First().DeliveredAt);
        reopened.Save(first with { LeadMinutes = 10 });
        Equal(0, reopened.TakeDue(expected.AddMinutes(19)).Length);
        // A later check can run on wake/restart and sends every due reminder once.
        Equal(2, reopened.TakeDue(Utc(26, 14, 5)).Length);
        Equal(0, reopened.TakeDue(Utc(26, 14, 6)).Length);
        reopened.Remove(first.Id);
        Equal(1, reopened.CountOn(date));
        var batch = new ReminderStore(Path.GetDirectoryName(store.FilePath)!);
        batch.Load();
        Equal(second.Id, batch.ForDate(date).Single().Id);
        Equal(0, batch.TakeDue(Utc(26, 14, 6)).Length);

        const string unsupported = """{"schemaVersion":2,"entries":[]}""";
        File.WriteAllText(store.FilePath, unsupported);
        var broken = new ReminderStore(Path.GetDirectoryName(store.FilePath)!);
        broken.Load();
        if (broken.ReadError is null) throw new InvalidOperationException("高版本提醒文件未报错");
        Throws<IOException>(() => broken.Save(first));
        Equal(unsupported, File.ReadAllText(store.FilePath));

        var blocked = new ReminderStore(Path.Combine(directory, "reminder-conflict"));
        Directory.CreateDirectory(blocked.FilePath);
        Throws<UnauthorizedAccessException>(() => blocked.Save(first));
        Equal(0, blocked.CountOn(date));
        Console.WriteLine("通过：日期提醒提前量、跨午夜、重启和补发去重、改期、删除及文件写入失败。");
    }

    private static DateTimeOffset Utc(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"期望 {typeof(T).Name}");
    }
}
