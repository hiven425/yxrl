using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Yxrl.Capture;

internal sealed class RecordingTrackView : StackPanel
{
    private readonly MediaPlayer player = new();
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100, Value = 100, Width = 112, TickFrequency = 1 };
    private readonly CheckBox muted = new();
    private readonly Uri source;
    private bool ready, playWhenReady;
    private TimeSpan wanted;
    private double speed = 1;

    public RecordingTrackView(string label, string path, Action<string> report)
    {
        source = new Uri(path);
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 3, 14, 3);
        var title = new TextBlock { Text = label, FontWeight = FontWeights.Medium, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Children.Add(title);
        Children.Add(volume);
        AutomationProperties.SetName(volume, label + "音量");
        var percent = new TextBlock { Text = "100%", MinWidth = 38, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
        percent.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Typography.SetNumeralAlignment(percent, FontNumeralAlignment.Tabular);
        Children.Add(percent);
        muted.Content = "静音";
        muted.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(muted, label + "静音");
        Children.Add(muted);
        void Adjust()
        {
            percent.Text = volume.Value.ToString("0", CultureInfo.CurrentCulture) + "%";
            player.Volume = Gain;
        }
        volume.ValueChanged += (_, _) => Adjust();
        muted.Checked += (_, _) => Adjust();
        muted.Unchecked += (_, _) => Adjust();
        player.MediaOpened += (_, _) =>
        {
            ready = true;
            Adjust();
            Sync(wanted, speed, playWhenReady);
        };
        player.MediaFailed += (_, _) => report($"{label}无法回放，仍可尝试导出。");
    }

    public double Gain => muted.IsChecked == true ? 0 : volume.Value / 100;
    public void Open() => player.Open(source);

    public void Sync(TimeSpan position, double ratio, bool play)
    {
        wanted = position;
        speed = ratio;
        playWhenReady = play;
        if (!ready)
            return;
        player.SpeedRatio = ratio;
        player.Position = position;
        if (play) player.Play();
        else player.Pause();
    }

    public void Drift(TimeSpan position)
    {
        wanted = position;
        if (ready && Math.Abs((player.Position - position).TotalMilliseconds) > 220)
            player.Position = position;
    }

    public void Close() => player.Close();
}

internal sealed class CuePreview(int width, int height, CueTimeline timeline) : FrameworkElement
{
    private long time;
    private bool mouse, keys;

    public void Set(long position, bool showMouse, bool showKeys)
    {
        time = position;
        mouse = showMouse;
        keys = showKeys;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth / width, ActualHeight / height);
        if (scale <= 0) return;
        double x0 = (ActualWidth - width * scale) / 2, y0 = (ActualHeight - height * scale) / 2;
        double unit = CueTimeline.Units(height) * scale;
        var font = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Text(string value, Brush brush) => new(value, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, font, 18 * unit, brush, dpi);
        foreach (var (cue, age) in timeline.Visible(time, mouse, keys))
        {
            if (cue.Kind == CueKind.Shortcut && cue.Label is { } label)
            {
                byte opacity = (byte)(224 * Math.Min(1, (1 - age) * 5));
                var text = Text(label, new SolidColorBrush(Color.FromArgb(opacity, 255, 255, 255)));
                double left = (ActualWidth - text.Width) / 2, top = y0 + height * scale - 37 * unit;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(opacity, 16, 28, 44)), null,
                    new Rect(left - 9 * unit, top, text.Width + 18 * unit, 27 * unit), 5 * unit, 5 * unit);
                dc.DrawText(text, new(left, top + (27 * unit - text.Height) / 2));
            }
            else if (cue.Kind != CueKind.Shortcut)
            {
                var point = new Point(x0 + cue.X * scale, y0 + cue.Y * scale);
                var (radius, opacity) = CueTimeline.Ring(age, unit);
                var rgba = CueTimeline.Color(cue.Kind, opacity);
                var brush = new SolidColorBrush(Color.FromArgb(rgba.Alpha, rgba.Red, rgba.Green, rgba.Blue));
                dc.DrawEllipse(null, new Pen(brush, 3 * unit), point, radius, radius);
                if (CueTimeline.Wheel(cue.Kind) is { } mark)
                {
                    var text = Text(mark, brush);
                    dc.DrawText(text, new(point.X - text.Width / 2, point.Y - text.Height / 2));
                }
            }
        }
    }
}
