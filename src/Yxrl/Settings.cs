using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yxrl;

internal sealed record ClockOptions(bool Enabled = true, string Preset = "seconds", string Top = "HH:mm:ss",
    string Bottom = "M月d日 ddd", string? Target = "spring");
internal sealed record CalendarOptions(bool ShowLunar = true, bool ShowAdjustments = true, bool ShowOutside = false,
    bool Hour12 = false, bool ShowWeeks = true, int WeekStart = 1, DateOnly? From = null, DateOnly? To = null)
{
    public bool InWeekRange(DateOnly day) => ShowWeeks && !(day < From) && !(day > To);
    public int? Week(DateOnly day) => InWeekRange(day) ? ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue)) : null;

    public int? RowWeek(DateOnly start)
    {
        var end = start.AddDays(6);
        if (!ShowWeeks || end < From || start > To) return null;
        var day = start.AddDays((4 - (int)start.DayOfWeek + 7) % 7);
        if (From is { } first && day < first) day = first;
        if (To is { } last && day > last) day = last;
        return Week(day);
    }
}
internal sealed record City(long Id, string Name, double Latitude, double Longitude, string Region = "", string Timezone = "")
{
    public string Label => string.IsNullOrWhiteSpace(Region) ? Name : $"{Name} · {Region}";
    internal bool HasValidLocation => !string.IsNullOrWhiteSpace(Name) && double.IsFinite(Latitude) && Math.Abs(Latitude) <= 90
        && double.IsFinite(Longitude) && Math.Abs(Longitude) <= 180;
}
internal sealed record Preferences(ClockOptions Clock, CalendarOptions Calendar, City? City, bool DirectWeatherConnection = false)
{
    public Capture.CaptureOptions Capture { get; init; } = new();
    public Capture.TranslationOptions Translation { get; init; } = new();
}
internal sealed class SettingsStore(string directory)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    private JsonObject original = new();
    public string? ReadError { get; private set; }
    public Preferences Current { get; private set; } = new(new(), new(), null);
    public bool CloseOnOutsideClick => original["general"]?["closeOnBlur"]?.GetValue<bool>() ?? true;
    public HashSet<string> Disabled { get; private set; } = [];
    public FestivalRule[] CustomRules { get; private set; } = [];

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var doc = JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject() ?? throw new JsonException("空文件");
            if ((doc["schemaVersion"]?.GetValue<int>() ?? 1) != 1) throw new JsonException("无法读取此版本的配置");
            var ck = doc["clock"]; var cal = doc["calendar"]; var range = cal?["weekRange"];
            var lines = ck?["lines"]?.Deserialize<string[]>();
            if (lines is not null && (lines.Length != 2 || lines.Any(x => x is null))) throw new JsonException("时钟需要两行格式");
            var clock = new ClockOptions(Bool(ck, "enabled", true), ck?["preset"]?.GetValue<string>() ?? "seconds",
                lines?[0] ?? "HH:mm:ss", lines?[1] ?? "M月d日 ddd",
                ck is JsonObject obj && obj.ContainsKey("countdownFestivalId") ? ck["countdownFestivalId"]?.GetValue<string>() : "spring");
            ClockFormatter.Validate(clock);
            var calendar = new CalendarOptions(Bool(cal,"showLunar",true), Bool(cal,"showAdjustments",true),
                Bool(cal,"showOutside",false), Bool(cal,"hour12",false), Bool(cal,"showWeeks",true),
                cal?["weekStart"]?.GetValue<int>() ?? 1, range?["from"]?.Deserialize<DateOnly>(), range?["to"]?.Deserialize<DateOnly>());
            ValidateCalendar(calendar);
            if (range is not null && calendar.From is null) throw new JsonException("周数范围缺少起止日期");
            var city = doc["weather"]?["location"]?.Deserialize<City>(Json);
            if (city is { HasValidLocation: false })
                throw new JsonException("城市位置无效");
            _ = Bool(doc["general"], "closeOnBlur", true);
            var disabled = doc["festivals"]?["disabledIds"]?.Deserialize<string[]>() ?? [];
            var custom = doc["festivals"]?["customRules"]?.Deserialize<FestivalRule[]>(Json) ?? [];
            if (custom.Any(x => x is null || !x.Valid)) throw new JsonException("自定义节日规则无效");
            Disabled = disabled.ToHashSet();
            CustomRules = custom;
            var capture = doc["capture"]?.Deserialize<Capture.CaptureOptions>(Json) ?? new();
            var translation = doc["translation"]?.Deserialize<Capture.TranslationOptions>(Json) ?? new();
            capture.Validate(); translation.Validate();
            Current = new(clock, calendar, city, Bool(doc["weather"], "directConnection", false)) { Capture = capture, Translation = translation };
            original = doc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            ReadError = $"配置读取失败，已保留原件。{FilePath}\n{ex.Message}";
        }
    }
    private static bool Bool(JsonNode? node, string name, bool fallback) => node?[name]?.GetValue<bool>() ?? fallback;
    public static void ValidateCalendar(CalendarOptions cal)
    {
        if (cal.WeekStart is < 0 or > 6 || cal.From.HasValue != cal.To.HasValue || cal.To < cal.From)
            throw new FormatException("请选择完整的周数起止日期，结束日期应晚于或等于开始日期。");
    }

    public void Save(Preferences next)
    {
        if (ReadError is not null) throw new IOException(ReadError);
        ClockFormatter.Validate(next.Clock);
        ValidateCalendar(next.Calendar);
        next.Capture.Validate(); next.Translation.Validate();
        var doc = (JsonObject)original.DeepClone();
        JsonObject Section(string name) => (JsonObject)(doc[name] ??= new JsonObject());
        doc["schemaVersion"] = 1;
        var clock = Section("clock");
        clock["enabled"] = next.Clock.Enabled; clock["preset"] = next.Clock.Preset;
        clock["lines"] = new JsonArray(next.Clock.Top, next.Clock.Bottom);
        clock["countdownFestivalId"] = next.Clock.Target;
        var cal = Section("calendar"); var c = next.Calendar;
        cal["showLunar"] = c.ShowLunar; cal["showAdjustments"] = c.ShowAdjustments;
        cal["showOutside"] = c.ShowOutside; cal["hour12"] = c.Hour12;
        cal["showWeeks"] = c.ShowWeeks; cal["weekStart"] = c.WeekStart;
        cal["weekRange"] = c.From is null ? null : JsonSerializer.SerializeToNode(new { from = c.From, to = c.To }, Json);
        var weather = Section("weather");
        weather["location"] = JsonSerializer.SerializeToNode(next.City, Json);
        weather["directConnection"] = next.DirectWeatherConnection;
        var capture = Section("capture");
        capture["enabled"] = next.Capture.Enabled;
        var shortcuts = (JsonObject)(capture["shortcuts"] ??= new JsonObject());
        foreach (var action in Enum.GetValues<Capture.ShotAction>())
            shortcuts[JsonNamingPolicy.CamelCase.ConvertName(action.ToString())] = next.Capture.Shortcuts[action];
        var translation = Section("translation");
        translation["baseUrl"] = next.Translation.BaseUrl;
        translation["model"] = next.Translation.Model;
        translation["targetLanguage"] = next.Translation.TargetLanguage;
        Directory.CreateDirectory(DirectoryPath);
        WriteAtomic(FilePath, doc.ToJsonString(Json));
        original = doc;
        Current = next;
    }
    internal static void WriteAtomic(string path, string data)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, data); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
