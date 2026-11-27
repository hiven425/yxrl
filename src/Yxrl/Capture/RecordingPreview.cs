using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed class RecordingPreview : Window
{
    private readonly RecordedClip clip;
    internal RecordedClip SourceClip => clip;
    private readonly MediaElement video = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform };
    private readonly CuePreview cues;
    private readonly RecordingTrackView? systemTrack, microphoneTrack;
    private readonly CheckBox mouse = new() { Content = "鼠标动画", IsChecked = true }, keys = new() { Content = "快捷键动画", IsChecked = true };
    private readonly Slider position = new();
    private readonly TextBox first = new() { Width = 75 }, last = new() { Width = 75 };
    private readonly TextBox[] crop = [MarginInput(), MarginInput(), MarginInput(), MarginInput()];
    private readonly TextBlock time = new(), status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button play = new() { Content = "播放", IsEnabled = false }, save = new() { Content = "另存为" },
        cancel = new() { Content = "取消导出", Visibility = Visibility.Collapsed };
    private readonly ComboBox format = new(), speed = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private CancellationTokenSource? export;
    private Task? exportJob;
    private bool playing, updatingSeek, saved, forceClose, preserve;

    public RecordingPreview(RecordedClip clip, Action repeat)
    {
        this.clip = clip;
        Title = "YXRL 录屏回放";
        if (Views.Theme.AppIcon is { } appIcon) Icon = appIcon;
        AutomationProperties.SetAutomationId(this, "YxrlRecordingPreview");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var work = SystemParameters.WorkArea;
        Width = Math.Min(720, work.Width);
        Height = Math.Min(600, work.Height);
        MinWidth = Math.Min(440, Width);
        MinHeight = Math.Min(335, Height);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Ink");
        var frame = new DockPanel { Margin = new Thickness(14), LastChildFill = true };
        Content = frame;
        var controls = new StackPanel();
        DockPanel.SetDock(controls, Dock.Bottom);
        frame.Children.Add(controls);
        var picture = new Grid();
        picture.Children.Add(video);
        cues = new CuePreview(clip.Width, clip.Height, new CueTimeline(clip.Cues ?? [])) { IsHitTestVisible = false };
        picture.Children.Add(cues);
        frame.Children.Add(new Border { Background = Brushes.Black, Child = picture, CornerRadius = new CornerRadius(6) });

        position.Maximum = clip.Duration.TotalSeconds;
        position.IsMoveToPointEnabled = true;
        position.IsEnabled = false;
        position.ValueChanged += (_, _) =>
        {
            if (!updatingSeek && position.IsEnabled)
            {
                video.Position = TimeSpan.FromSeconds(position.Value);
                SyncTracks(video.Position, playing);
                UpdateCues();
            }
            time.Text = $"{RecordingWindow.TimeLabel(TimeSpan.FromSeconds(position.Value))} / {RecordingWindow.TimeLabel(clip.Duration)}";
        };
        AutomationProperties.SetName(position, "回放进度");
        controls.Children.Add(position);
        var transport = new WrapPanel { Margin = new Thickness(0, 4, 0, 7) };
        controls.Children.Add(transport);
        play.Click += (_, _) => TogglePlayback();
        Add(transport, play);
        time.VerticalAlignment = VerticalAlignment.Center;
        time.Text = "00:00 / " + RecordingWindow.TimeLabel(clip.Duration);
        transport.Children.Add(time);
        var trim = new WrapPanel { Margin = new Thickness(0, 2, 0, 7) };
        controls.Children.Add(trim);
        AddTime(trim, "开始（秒）", first, 0);
        AddTime(trim, "结束（秒）", last, clip.Duration.TotalSeconds);
        var spatial = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
        controls.Children.Add(spatial);
        string[] sides = ["裁左", "裁上", "裁右", "裁下"];
        for (int i = 0; i < sides.Length; i++)
        {
            AutomationProperties.SetName(crop[i], sides[i] + "（像素）");
            AddField(spatial, sides[i], crop[i]);
            crop[i].TextChanged += (_, _) => UpdateCropPreview();
        }
        foreach (double ratio in new[] { .25, .5, 1, 1.25, 1.5, 2 })
            speed.Items.Add(new ComboBoxItem { Tag = ratio, Content = $"{ratio:0.##}×" });
        speed.SelectedIndex = 2;
        AutomationProperties.SetName(speed, "倍速");
        speed.SelectionChanged += (_, _) =>
        {
            if (speed.SelectedItem is ComboBoxItem { Tag: double ratio })
            {
                video.SpeedRatio = ratio;
                SyncTracks(video.Position, playing);
            }
        };
        AddField(spatial, "倍速", speed);
        var soundEffects = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (clip.Tracks?.System is { } systemPath)
        {
            systemTrack = new("系统声音", systemPath, message => SetStatus(message, true));
            soundEffects.Children.Add(systemTrack);
        }
        if (clip.Tracks?.Microphone is { } microphonePath)
        {
            microphoneTrack = new("麦克风", microphonePath, message => SetStatus(message, true));
            soundEffects.Children.Add(microphoneTrack);
        }
        foreach (var (button, condition) in new[] { (mouse, clip.Cues?.Any(c => c.Kind != CueKind.Shortcut) == true),
            (keys, clip.Cues?.Any(c => c.Kind == CueKind.Shortcut) == true) })
        {
            if (!condition) continue;
            button.Margin = new Thickness(0, 4, 12, 4);
            button.Checked += (_, _) => UpdateCues();
            button.Unchecked += (_, _) => UpdateCues();
            soundEffects.Children.Add(button);
        }
        if (soundEffects.Children.Count > 0)
            controls.Children.Add(new Expander { Header = "音轨与动画", IsExpanded = true, Content = soundEffects, Margin = new Thickness(0, 0, 0, 5) });
        var actions = new WrapPanel();
        controls.Children.Add(actions);
        foreach (string name in new[] { "MP4", "GIF", "WebP" }) format.Items.Add(name);
        format.SelectedIndex = 0;
        AutomationProperties.SetName(format, "导出格式");
        Add(actions, format);
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        Add(actions, save);
        Add(actions, cancel);
        var again = new Button { Content = "重新录制" };
        Add(actions, again);
        again.Click += (_, _) =>
        {
            if (export is not null || (!saved && !ConfirmDiscard()))
                return;
            forceClose = true;
            Close();
            repeat();
        };
        save.Click += async (_, _) => await Save();
        cancel.Click += (_, _) => export?.Cancel();
        status.Margin = new Thickness(0, 7, 0, 0);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        controls.Children.Add(status);
        status.Text = $"{clip.Width} × {clip.Height} px · {clip.Fps} 帧/秒" + (clip.HasAudio ? " · 有声音" : "");

        video.MediaOpened += (_, _) =>
        {
            video.Pause();
            play.IsEnabled = position.IsEnabled = true;
            UpdateCropPreview();
            ReadPosition();
            SyncTracks(video.Position, false);
        };
        video.SizeChanged += (_, _) => UpdateCropPreview();
        video.MediaEnded += (_, _) => PausePlayback();
        video.MediaFailed += (_, e) => SetStatus("Windows 无法预览此视频，可继续另存。 " + e.ErrorException.Message, true);
        timer.Tick += (_, _) =>
        {
            ReadPosition();
            UpdateCues();
            if (!video.IsBuffering)
            {
                systemTrack?.Drift(video.Position);
                microphoneTrack?.Drift(video.Position);
            }
            if (TryRange(out _, out var end) && video.Position >= end)
                PausePlayback();
        };
        Loaded += (_, _) =>
        {
            video.Source = new Uri(clip.Path);
            video.Play();
            systemTrack?.Open();
            microphoneTrack?.Open();
        };
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                await Save();
            }
        };
        Closing += (_, e) =>
        {
            if (!forceClose && (!saved || export is not null))
                e.Cancel = !ConfirmDiscard();
        };
        Closed += (_, _) => ReleaseClip();
    }

    private static void Add(WrapPanel panel, Control control)
    {
        control.Margin = new Thickness(0, 0, 8, 4);
        control.MinHeight = 28;
        panel.Children.Add(control);
    }

    private static TextBox MarginInput() => new() { Width = 40, Text = "0", MaxLength = 5 };

    private static void AddField(WrapPanel panel, string label, Control input)
    {
        var field = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 11, 4) };
        field.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) });
        field.Children.Add(input);
        panel.Children.Add(field);
    }

    private bool TryEdits(out RecordingEdits edits)
    {
        edits = RecordingEdits.Whole(clip);
        var margins = new int[crop.Length];
        for (int i = 0; i < margins.Length; i++)
            if (!int.TryParse(crop[i].Text, CultureInfo.CurrentCulture, out margins[i]) || margins[i] < 0)
                return false;
        var bounds = new PixelRect(margins[0], margins[1], clip.Width - margins[2], clip.Height - margins[3]);
        if (bounds.Width < Mp4Writer.MinimumEdge || bounds.Height < Mp4Writer.MinimumEdge)
            return false;
        bounds = bounds with { Right = bounds.Right - bounds.Width % 2, Bottom = bounds.Bottom - bounds.Height % 2 };
        edits = new(bounds, (double)((ComboBoxItem)speed.SelectedItem).Tag,
            new RecordingMix(systemTrack?.Gain ?? 1, microphoneTrack?.Gain ?? 1),
            mouse.IsChecked == true, keys.IsChecked == true);
        return true;
    }

    private void UpdateCropPreview()
    {
        if (video.ActualWidth <= 0 || video.ActualHeight <= 0 || !TryEdits(out var edits))
            return;
        double scale = Math.Min(video.ActualWidth / clip.Width, video.ActualHeight / clip.Height);
        double x = (video.ActualWidth - clip.Width * scale) / 2, y = (video.ActualHeight - clip.Height * scale) / 2;
        var c = edits.Crop;
        var visible = new RectangleGeometry(new Rect(x + c.Left * scale, y + c.Top * scale, c.Width * scale, c.Height * scale));
        video.Clip = cues.Clip = visible;
    }

    private void AddTime(WrapPanel panel, string label, TextBox field, double seconds)
    {
        var value = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        AutomationProperties.SetName(field, label);
        value.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        field.Text = seconds.ToString("0.###", CultureInfo.CurrentCulture);
        value.Children.Add(field);
        var now = new Button { Content = "用当前", ToolTip = $"{label}设为播放位置" };
        AutomationProperties.SetName(now, now.ToolTip.ToString()!);
        now.Click += (_, _) => field.Text = position.Value.ToString("0.###", CultureInfo.CurrentCulture);
        value.Children.Add(now);
        panel.Children.Add(value);
    }

    private void ReadPosition()
    {
        updatingSeek = true;
        position.Value = Math.Clamp(video.Position.TotalSeconds, 0, position.Maximum);
        updatingSeek = false;
    }

    private void TogglePlayback()
    {
        if (playing)
        {
            PausePlayback();
            return;
        }
        if (!TryRange(out var start, out var end))
        {
            SetStatus("请输入有效的开始和结束时间。", true);
            return;
        }
        if (!TryEdits(out _))
        {
            SetStatus($"请输入有效的裁剪边距，至少保留 {Mp4Writer.MinimumEdge} × {Mp4Writer.MinimumEdge} 像素。", true);
            return;
        }
        if (video.Position < start || video.Position >= end)
            video.Position = start;
        video.Play();
        timer.Start();
        playing = true;
        SyncTracks(video.Position, true);
        play.Content = "暂停播放";
    }

    private void PausePlayback()
    {
        video.Pause();
        timer.Stop();
        playing = false;
        SyncTracks(video.Position, false);
        play.Content = "播放";
    }

    private void SyncTracks(TimeSpan time, bool play)
    {
        double ratio = video.SpeedRatio;
        systemTrack?.Sync(time, ratio, play);
        microphoneTrack?.Sync(time, ratio, play);
    }

    private void UpdateCues() => cues.Set(TimeSpan.FromSeconds(position.Value).Ticks, mouse.IsChecked == true, keys.IsChecked == true);

    private bool TryRange(out TimeSpan from, out TimeSpan to)
    {
        from = to = TimeSpan.Zero;
        if (!double.TryParse(first.Text, CultureInfo.CurrentCulture, out double start) ||
            !double.TryParse(last.Text, CultureInfo.CurrentCulture, out double end) ||
            !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end > clip.Duration.TotalSeconds + .001 ||
            end - start < 1.0 / clip.Fps - .001)
            return false;
        from = TimeSpan.FromSeconds(start);
        to = Math.Abs(end - clip.Duration.TotalSeconds) <= .001 ? clip.Duration : TimeSpan.FromSeconds(end);
        return to - from >= TimeSpan.FromTicks(TimeSpan.TicksPerSecond / clip.Fps);
    }

    private async Task Save()
    {
        if (export is not null)
            return;
        if (!TryRange(out var start, out var end))
        {
            SetStatus("请输入有效的开始和结束时间，至少保留一帧。", true);
            return;
        }
        if (!TryEdits(out var edits))
        {
            SetStatus($"请输入有效的裁剪边距，至少保留 {Mp4Writer.MinimumEdge} × {Mp4Writer.MinimumEdge} 像素。", true);
            return;
        }
        var kind = (RecordingFormat)format.SelectedIndex;
        string extension = kind.ToString().ToLowerInvariant();
        var dialog = new SaveFileDialog
        {
            Title = "保存录屏", Filter = $"{kind} 文件 (*.{extension})|*.{extension}", DefaultExt = "." + extension,
            FileName = $"YXRL-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}", AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;
        PausePlayback();
        using var work = new CancellationTokenSource();
        export = work;
        save.IsEnabled = false;
        cancel.Visibility = Visibility.Visible;
        SetStatus("正在导出…");
        var progress = new Progress<double>(fraction =>
        {
            if (export == work && !work.IsCancellationRequested)
                SetStatus($"正在导出 {fraction:P0}");
        });
        try
        {
            exportJob = Task.Run(() => RecordingExport.Save(clip, dialog.FileName, kind, start, end, work.Token, progress, edits));
            await exportJob;
            saved = true;
            SetStatus($"已保存 {Path.GetFileName(dialog.FileName)}");
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            SetStatus("已取消导出。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException
            or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            SetStatus("导出失败。 " + ex.Message, true);
        }
        finally
        {
            exportJob = null;
            export = null;
            save.IsEnabled = true;
            cancel.Visibility = Visibility.Collapsed;
        }
    }

    private void SetStatus(string message, bool warning = false)
    {
        status.Text = message;
        status.SetResourceReference(TextBlock.ForegroundProperty, warning ? "Danger" : "Muted");
    }

    private bool ConfirmDiscard() => MessageBox.Show(this, "关闭回放并清理本次录制的临时文件？", Title,
        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void ReleaseClip()
    {
        timer.Stop();
        video.Close();
        systemTrack?.Close();
        microphoneTrack?.Close();
        export?.Cancel();
        if (preserve)
            return;
        void Clean()
        {
            if (clip.Session is { } session) session.Delete();
            else RecordingFiles.Remove(clip.Path);
        }
        if (exportJob is { } job)
            _ = job.ContinueWith(_ => Clean(), TaskScheduler.Default);
        else
            Clean();
    }

    public void Shutdown(bool preserveRecording = true)
    {
        forceClose = true;
        preserve = preserveRecording && !saved;
        Close();
    }
}
