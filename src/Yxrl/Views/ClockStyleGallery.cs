using System.ComponentModel;

namespace Yxrl.Views;

internal sealed class ClockStyleTile(ClockStyle style, ClockText example) : INotifyPropertyChanged
{
    public string Id => style.Id;
    public string Name => style.Name;
    public ClockText Example { get; } = example;
    public ClockText Lines { get; private set; } = example;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Show(ClockText lines)
    {
        if (Lines == lines) return;
        Lines = lines;
        PropertyChanged?.Invoke(this, new(nameof(Lines)));
    }
}

internal sealed class ClockStyleGallery
{
    public IReadOnlyList<ClockStyleTile> Tiles { get; }

    public ClockStyleGallery()
    {
        // The catalogue has its own date, built-in holidays and illustrative weather.
        // Opening a preview never changes the user's city or populates the weather cache.
        var calendar = new CalendarService();
        var date = new DateTime(2026, 11, 26, 12, 25, 31);
        var stamp = DateTimeOffset.UtcNow;
        var city = new City(1795565, "深圳", 22.54, 114.05);
        var exampleWeather = new WeatherReading(
            new(23, 20, 26, "多云", "2026-11-26T12:00", stamp),
            new(42, "2026-11-26T12:00", stamp));
        Tiles = ClockFormatter.Styles.Where(style => style.Id != "custom").Select(style =>
        {
            var sample = new Preferences(new(Preset: style.Id), new(), city);
            return new ClockStyleTile(style, ClockFormatter.Render(date, sample, calendar, exampleWeather));
        }).ToArray();
    }

    public void ShowExamples()
    {
        foreach (var tile in Tiles) tile.Show(tile.Example);
    }

    public void ShowCurrent(DateTime now, Preferences draft, CalendarService calendar, WeatherReading? weather)
    {
        foreach (var tile in Tiles)
        {
            var options = draft with { Clock = draft.Clock with { Preset = tile.Id } };
            tile.Show(ClockFormatter.Render(now, options, calendar, weather));
        }
    }
}
