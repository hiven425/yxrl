using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Yxrl.Desktop;
using Yxrl.Views;

namespace Yxrl.Capture;

internal sealed class RecordingPreview : Window
{
    private readonly RecordedClip clip;
    internal RecordedClip SourceClip => clip;
    private readonly MediaElement video = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform };
    private readonly CuePreview cues;
    private readonly CropOverlay cropOverlay = new();
    private readonly ProgressPreview progressOverlay = new();
    private readonly TrimRangeBar trimBar = new();
    private readonly RecordingTrackView? systemTrack, microphoneTrack;
    private readonly CheckBox mouse = new() { Content = "鼠标动画", IsChecked = true }, keys = new() { Content = "快捷键动画", IsChecked = true },
        hardwareAccel = new() { Content = "硬件加速" }, closeAfterExport = new() { Content = "导出后关闭" };
    private readonly Slider position = new();
    private readonly TextBox first = new() { Width = 70, MinHeight = 28, Padding = new Thickness(6, 3, 6, 3) },
        last = new() { Width = 70, MinHeight = 28, Padding = new Thickness(6, 3, 6, 3) };
    private readonly TextBox[] crop = [MarginInput(), MarginInput(), MarginInput(), MarginInput()];
    private readonly TextBlock time = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
        status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse statusDot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
    private readonly Button play = new() { Content = "播放", IsEnabled = false },
        save = new() { Content = "另存为" }, quickSave = new() { Content = "快存" },
        copyFile = new() { Content = "复制文件" }, openFolder = new() { Content = "打开位置", Visibility = Visibility.Collapsed },
        resetCrop = new() { Content = "重置", IsEnabled = false },
        cancel = new() { Content = "取消导出", Visibility = Visibility.Collapsed };
    private readonly ComboBox format = new(), speed = new(), progressStyle = new(), exportQuality = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private CancellationTokenSource? export;
    private Task? exportJob;
    private string? lastSavedPath;
    private bool playing, updatingSeek, updatingRange, updatingCrop, saved, forceClose, preserve, micaEnabled;

    public RecordingPreview(RecordedClip clip, Action repeat)
    {
        this.clip = clip;
        Title = "YXRL 录屏回放";
        if (Theme.AppIcon is { } appIcon) Icon = appIcon;
        AutomationProperties.SetAutomationId(this, "YxrlRecordingPreview");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var work = SystemParameters.WorkArea;
        Width = Math.Min(840, work.Width);
        Height = Math.Min(700, work.Height);
        MinWidth = Math.Min(540, Width);
        MinHeight = Math.Min(390, Height);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Ink");

        var frame = new DockPanel { Margin = new Thickness(14, 12, 14, 12), LastChildFill = true };
        Content = frame;
        var controls = new StackPanel();
        DockPanel.SetDock(controls, Dock.Bottom);
        frame.Children.Add(controls);

        // Video stage + cues + progress + draggable 4-edge crop overlay
        var picture = new Grid();
        picture.Children.Add(video);
        cues = new CuePreview(clip.Width, clip.Height, new CueTimeline(clip.Cues ?? [])) { IsHitTestVisible = false };
        picture.Children.Add(cues);
        picture.Children.Add(progressOverlay);
        picture.Children.Add(cropOverlay);
        cropOverlay.CropChanged += bounds =>
        {
            updatingCrop = true;
            try
            {
                crop[0].Text = bounds.Left.ToString(CultureInfo.CurrentCulture);
                crop[1].Text = bounds.Top.ToString(CultureInfo.CurrentCulture);
                crop[2].Text = (clip.Width - bounds.Right).ToString(CultureInfo.CurrentCulture);
                crop[3].Text = (clip.Height - bounds.Bottom).ToString(CultureInfo.CurrentCulture);
            }
            finally { updatingCrop = false; }
            UpdateCropPreview();
        };
        var theater = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 21, 29)),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10),
            ClipToBounds = true,
            Child = picture
        };
        theater.SetResourceReference(Border.BorderBrushProperty, "Line");
        frame.Children.Add(theater);

        // Playback transport + visual trim range card
        position.Maximum = clip.Duration.TotalSeconds;
        position.IsMoveToPointEnabled = true;
        position.IsEnabled = false;
        position.Margin = new Thickness(10, 0, 10, 0);
        position.ValueChanged += (_, _) =>
        {
            if (!updatingSeek && !updatingRange && position.IsEnabled)
            {
                video.Position = TimeSpan.FromSeconds(position.Value);
                SyncTracks(video.Position, playing);
                UpdateCues();
            }
            time.Text = $"{RecordingWindow.TimeLabel(TimeSpan.FromSeconds(position.Value))} / {RecordingWindow.TimeLabel(clip.Duration)}";
            SyncTrimBar();
        };
        AutomationProperties.SetName(position, "回放进度");

        play.SetResourceReference(StyleProperty, "SecondaryButton");
        play.MinHeight = 28;
        play.Padding = new Thickness(12, 3, 12, 3);
        play.Click += (_, _) => TogglePlayback();

        time.Text = "00:00 / " + RecordingWindow.TimeLabel(clip.Duration);
        Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        time.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var timePill = new Border
        {
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = time
        };
        timePill.SetResourceReference(Border.BackgroundProperty, "Card");
        timePill.SetResourceReference(Border.BorderBrushProperty, "Line");

        var transportDock = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(play, Dock.Left);
        DockPanel.SetDock(timePill, Dock.Right);
        transportDock.Children.Add(play);
        transportDock.Children.Add(timePill);
        transportDock.Children.Add(position);

        trimBar.SetValues(clip.Duration.TotalSeconds, 0, clip.Duration.TotalSeconds, 0);
        trimBar.Margin = new Thickness(0, 6, 0, 0);
        trimBar.RangeChanged += (s, e, draggingStart) =>
        {
            updatingRange = true;
            try
            {
                first.Text = s.ToString("0.###", CultureInfo.CurrentCulture);
                last.Text = e.ToString("0.###", CultureInfo.CurrentCulture);
                if (position.IsEnabled)
                {
                    var target = TimeSpan.FromSeconds(draggingStart ? s : e);
                    video.Position = target;
                    SyncTracks(target, playing);
                    ReadPosition();
                    UpdateCues();
                }
            }
            finally { updatingRange = false; }
        };

        var transportStack = new StackPanel();
        transportStack.Children.Add(transportDock);
        transportStack.Children.Add(trimBar);
        var transportCard = new Border { Margin = new Thickness(0, 0, 0, 8), Child = transportStack };
        transportCard.SetResourceReference(StyleProperty, "GroupCard");
        transportCard.Padding = new Thickness(10, 7, 10, 7);
        controls.Children.Add(transportCard);

        // Trim, speed & crop editing card
        var editStack = new StackPanel();
        var trimRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (double ratio in new[] { .2, .25, .5, .75, 1, 1.25, 1.5, 2 })
            speed.Items.Add(new ComboBoxItem { Tag = ratio, Content = $"{ratio:0.##}×" });
        speed.SelectedIndex = 4;
        speed.MinHeight = 28;
        speed.Padding = new Thickness(8, 3, 24, 3);
        AutomationProperties.SetName(speed, "倍速");
        speed.SelectionChanged += (_, _) =>
        {
            if (speed.SelectedItem is ComboBoxItem { Tag: double ratio })
            {
                video.SpeedRatio = ratio;
                SyncTracks(video.Position, playing);
            }
        };
        var speedGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var speedLbl = new TextBlock { Text = "倍速", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        speedLbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        speedGroup.Children.Add(speedLbl);
        speedGroup.Children.Add(speed);
        DockPanel.SetDock(speedGroup, Dock.Right);
        trimRow.Children.Add(speedGroup);

        var trimTimes = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        AddTime(trimTimes, "开始（秒）", first, 0);
        AddTime(trimTimes, "结束（秒）", last, clip.Duration.TotalSeconds);
        first.TextChanged += (_, _) => { if (!updatingRange) { SyncTrimBar(); UpdateCues(); } };
        last.TextChanged += (_, _) => { if (!updatingRange) { SyncTrimBar(); UpdateCues(); } };
        trimRow.Children.Add(trimTimes);
        editStack.Children.Add(trimRow);

        var editSep = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 8) };
        editSep.SetResourceReference(Border.BackgroundProperty, "Line");
        editStack.Children.Add(editSep);

        var spatial = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        string[] sides = ["裁左", "裁上", "裁右", "裁下"];
        for (int i = 0; i < sides.Length; i++)
        {
            AutomationProperties.SetName(crop[i], sides[i] + "（像素）");
            AddField(spatial, sides[i], crop[i]);
            crop[i].TextChanged += (_, _) => { if (!updatingCrop) UpdateCropPreview(); };
        }
        resetCrop.SetResourceReference(StyleProperty, "SecondaryButton");
        resetCrop.MinHeight = 26;
        resetCrop.Padding = new Thickness(8, 2, 8, 2);
        resetCrop.FontSize = 12;
        resetCrop.ToolTip = "重置画面裁剪边距";
        AutomationProperties.SetName(resetCrop, "重置裁剪");
        resetCrop.Click += (_, _) =>
        {
            updatingCrop = true;
            try { foreach (var box in crop) box.Text = "0"; }
            finally { updatingCrop = false; }
            UpdateCropPreview();
        };
        spatial.Children.Add(resetCrop);
        editStack.Children.Add(spatial);

        var editCard = new Border { Margin = new Thickness(0, 0, 0, 8), Child = editStack };
        editCard.SetResourceReference(StyleProperty, "GroupCard");
        editCard.Padding = new Thickness(12, 9, 12, 9);
        controls.Children.Add(editCard);

        // Audio tracks, cues, progress & export options
        var soundEffects = new WrapPanel { Margin = new Thickness(4, 2, 4, 2) };
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
            button.Margin = new Thickness(0, 3, 14, 3);
            button.Checked += (_, _) => UpdateCues();
            button.Unchecked += (_, _) => UpdateCues();
            soundEffects.Children.Add(button);
        }
        foreach (string value in new[] { "无进度", "底部进度条", "计时文字", "倒计时" }) progressStyle.Items.Add(value);
        progressStyle.SelectedIndex = 0;
        progressStyle.MinHeight = 28;
        progressStyle.Padding = new Thickness(8, 3, 24, 3);
        AutomationProperties.SetName(progressStyle, "显示进度");
        progressStyle.SelectionChanged += (_, _) => UpdateCues();
        AddLabeledOption(soundEffects, "显示进度", progressStyle);

        foreach (string value in new[] { "轻量", "标准", "高清" }) exportQuality.Items.Add(value);
        exportQuality.SelectedIndex = 1;
        exportQuality.MinHeight = 28;
        exportQuality.Padding = new Thickness(8, 3, 24, 3);
        AutomationProperties.SetName(exportQuality, "导出画质");
        AddLabeledOption(soundEffects, "画质", exportQuality);

        hardwareAccel.Margin = new Thickness(0, 3, 12, 3);
        closeAfterExport.Margin = new Thickness(0, 3, 4, 3);
        soundEffects.Children.Add(hardwareAccel);
        soundEffects.Children.Add(closeAfterExport);
        bool hasTrackOrCue = clip.Tracks?.Available == true || clip.Cues?.Length > 0;
        controls.Children.Add(new Expander
        {
            Header = "音轨、动效与导出选项",
            IsExpanded = hasTrackOrCue,
            Content = soundEffects,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Footer export bar + status
        var bottomDock = new DockPanel { Margin = new Thickness(0, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (string name in new[] { "MP4", "GIF", "WebP" }) format.Items.Add(name);
        format.SelectedIndex = 0;
        format.MinHeight = 30;
        format.Padding = new Thickness(9, 3, 24, 3);
        AutomationProperties.SetName(format, "导出格式");
        Add(actions, format);
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        save.MinHeight = 30;
        save.Padding = new Thickness(13, 4, 13, 4);
        Add(actions, save);
        quickSave.SetResourceReference(StyleProperty, "SecondaryButton");
        quickSave.MinHeight = 30;
        quickSave.Padding = new Thickness(11, 4, 11, 4);
        quickSave.ToolTip = "直接保存至 视频\\YXRL（Ctrl+Shift+S）";
        AutomationProperties.SetName(quickSave, "快速保存");
        Add(actions, quickSave);
        copyFile.SetResourceReference(StyleProperty, "SecondaryButton");
        copyFile.MinHeight = 30;
        copyFile.Padding = new Thickness(11, 4, 11, 4);
        copyFile.ToolTip = "导出文件并复制到剪贴板";
        AutomationProperties.SetName(copyFile, "复制文件");
        Add(actions, copyFile);
        openFolder.SetResourceReference(StyleProperty, "SecondaryButton");
        openFolder.MinHeight = 30;
        openFolder.Padding = new Thickness(10, 4, 10, 4);
        Add(actions, openFolder);
        cancel.SetResourceReference(StyleProperty, "SecondaryButton");
        cancel.MinHeight = 30;
        Add(actions, cancel);
        var again = new Button { Content = "重新录制", Margin = new Thickness(0) };
        again.SetResourceReference(StyleProperty, "SecondaryButton");
        again.MinHeight = 30;
        actions.Children.Add(again);
        DockPanel.SetDock(actions, Dock.Right);
        bottomDock.Children.Add(actions);

        var statusGroup = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 12, 0) };
        DockPanel.SetDock(statusDot, Dock.Left);
        statusGroup.Children.Add(statusDot);
        statusGroup.Children.Add(status);
        bottomDock.Children.Add(statusGroup);
        controls.Children.Add(bottomDock);

        again.Click += (_, _) =>
        {
            if (export is not null || (!saved && !ConfirmDiscard()))
                return;
            forceClose = true;
            Close();
            repeat();
        };
        save.Click += async (_, _) => await ExportWithDialog();
        quickSave.Click += async (_, _) => await QuickExport();
        copyFile.Click += async (_, _) => await CopyExportedFile();
        openFolder.Click += (_, _) => OpenSavedFolder();
        cancel.Click += (_, _) => export?.Cancel();
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        SetStatus($"{clip.Width} × {clip.Height} px · {clip.Fps} 帧/秒" + (clip.HasAudio ? " · 有声音" : ""));

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
                await ExportWithDialog();
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                e.Handled = true;
                await QuickExport();
            }
        };
        Closing += (_, e) =>
        {
            if (!forceClose && (!saved || export is not null))
                e.Cancel = !ConfirmDiscard();
        };
        Closed += (_, _) => ReleaseClip();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyChrome();
    }

    internal void RefreshTheme() => ApplyChrome();

    private void ApplyChrome()
    {
        micaEnabled = Theme.ApplyWindowChrome(this, Native.BackdropKind.Mica);
        if (micaEnabled) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "Surface");
    }

    private static void Add(Panel panel, Control control)
    {
        control.Margin = new Thickness(0, 0, 8, 0);
        control.MinHeight = 30;
        panel.Children.Add(control);
    }

    private static TextBox MarginInput()
    {
        var box = new TextBox { Width = 46, Text = "0", MaxLength = 5, MinHeight = 28, Padding = new Thickness(6, 3, 6, 3) };
        Typography.SetNumeralAlignment(box, FontNumeralAlignment.Tabular);
        return box;
    }

    private static void AddField(WrapPanel panel, string label, Control input)
    {
        var field = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var lbl = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        field.Children.Add(lbl);
        field.Children.Add(input);
        panel.Children.Add(field);
    }

    private static void AddLabeledOption(WrapPanel panel, string label, Control input)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 12, 3), VerticalAlignment = VerticalAlignment.Center };
        var lbl = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        row.Children.Add(lbl);
        row.Children.Add(input);
        panel.Children.Add(row);
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
            mouse.IsChecked == true, keys.IsChecked == true,
            (RecordingQuality)Math.Clamp(exportQuality.SelectedIndex, 0, 2),
            (RecordingProgressStyle)Math.Clamp(progressStyle.SelectedIndex, 0, 3),
            hardwareAccel.IsChecked == true);
        return true;
    }

    private void UpdateCropPreview()
    {
        if (!TryEdits(out var edits))
            return;
        cropOverlay.SetClip(clip.Width, clip.Height, edits.Crop);
        resetCrop.IsEnabled = edits.Crop != RecordingEdits.Whole(clip).Crop;
        if (video.ActualWidth > 0 && video.ActualHeight > 0)
        {
            double scale = Math.Min(video.ActualWidth / clip.Width, video.ActualHeight / clip.Height);
            double x = (video.ActualWidth - clip.Width * scale) / 2, y = (video.ActualHeight - clip.Height * scale) / 2;
            var c = edits.Crop;
            var visible = new RectangleGeometry(new Rect(x + c.Left * scale, y + c.Top * scale, c.Width * scale, c.Height * scale));
            video.Clip = cues.Clip = visible;
        }
        UpdateCues();
    }

    private void SyncTrimBar()
    {
        if (TryRange(out var start, out var end))
            trimBar.SetValues(clip.Duration.TotalSeconds, start.TotalSeconds, end.TotalSeconds, position.Value);
    }

    private void AddTime(WrapPanel panel, string label, TextBox field, double seconds)
    {
        var value = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(field, label);
        var lbl = new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        value.Children.Add(lbl);
        Typography.SetNumeralAlignment(field, FontNumeralAlignment.Tabular);
        field.Text = seconds.ToString("0.###", CultureInfo.CurrentCulture);
        value.Children.Add(field);
        var now = new Button { Content = "用当前", ToolTip = $"{label}设为播放位置", Margin = new Thickness(5, 0, 0, 0), MinHeight = 28, Padding = new Thickness(8, 2, 8, 2), FontSize = 12 };
        now.SetResourceReference(StyleProperty, "SecondaryButton");
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

    private void UpdateCues()
    {
        long posTicks = TimeSpan.FromSeconds(position.Value).Ticks;
        cues.Set(posTicks, mouse.IsChecked == true, keys.IsChecked == true);
        PixelRect cropRect = TryEdits(out var edits) ? edits.Crop : RecordingEdits.Whole(clip).Crop;
        var style = (RecordingProgressStyle)Math.Clamp(progressStyle.SelectedIndex, 0, 3);
        if (TryRange(out var from, out var to))
            progressOverlay.Set(clip.Width, clip.Height, cropRect, Math.Max(0, posTicks - from.Ticks), Math.Max(1, (to - from).Ticks), style);
        else
            progressOverlay.Set(clip.Width, clip.Height, cropRect, posTicks, Math.Max(1, clip.Duration.Ticks), style);
    }

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

    private async Task ExportWithDialog()
    {
        if (export is not null)
            return;
        var kind = (RecordingFormat)format.SelectedIndex;
        string extension = kind.ToString().ToLowerInvariant();
        var dialog = new SaveFileDialog
        {
            Title = "保存录屏", Filter = $"{kind} 文件 (*.{extension})|*.{extension}", DefaultExt = "." + extension,
            FileName = $"YXRL-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}", AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;
        await RunExport(kind, dialog.FileName, copyToClipboard: false);
    }

    private async Task QuickExport()
    {
        if (export is not null)
            return;
        var kind = (RecordingFormat)format.SelectedIndex;
        string target = RecordingWindow.UniqueQuickSavePath(kind.ToString().ToLowerInvariant());
        await RunExport(kind, target, copyToClipboard: false);
    }

    private async Task CopyExportedFile()
    {
        if (export is not null)
            return;
        var kind = (RecordingFormat)format.SelectedIndex;
        string ext = kind.ToString().ToLowerInvariant();
        string cacheDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YXRL", "ClipExports");
        Directory.CreateDirectory(cacheDir);
        string target = System.IO.Path.Combine(cacheDir, $"YXRL-{DateTime.Now:yyyyMMdd-HHmmss-fff}.{ext}");
        bool ok = await RunExport(kind, target, copyToClipboard: true);
        if (ok)
        {
            var files = new StringCollection { target };
            Clipboard.SetFileDropList(files);
            SetStatus($"已复制文件 {System.IO.Path.GetFileName(target)}，可直接粘贴");
            PruneExportCache(cacheDir, 10);
        }
    }

    private static void PruneExportCache(string directory, int keep)
    {
        try
        {
            foreach (var old in new DirectoryInfo(directory).EnumerateFiles("YXRL-*.*")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
            {
                RecordingFiles.Remove(old.FullName);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<bool> RunExport(RecordingFormat kind, string targetPath, bool copyToClipboard)
    {
        if (!TryRange(out var start, out var end))
        {
            SetStatus("请输入有效的开始和结束时间，至少保留一帧。", true);
            return false;
        }
        if (!TryEdits(out var edits))
        {
            SetStatus($"请输入有效的裁剪边距，至少保留 {Mp4Writer.MinimumEdge} × {Mp4Writer.MinimumEdge} 像素。", true);
            return false;
        }
        PausePlayback();
        using var work = new CancellationTokenSource();
        export = work;
        save.IsEnabled = quickSave.IsEnabled = copyFile.IsEnabled = false;
        cancel.Visibility = Visibility.Visible;
        SetStatus("正在导出…");
        var progress = new Progress<double>(fraction =>
        {
            if (export == work && !work.IsCancellationRequested)
                SetStatus($"正在导出 {fraction:P0}");
        });
        bool succeeded = false;
        try
        {
            exportJob = Task.Run(() => RecordingExport.Save(clip, targetPath, kind, start, end, work.Token, progress, edits));
            await exportJob;
            succeeded = true;
            if (!copyToClipboard)
            {
                saved = true;
                lastSavedPath = targetPath;
                openFolder.Visibility = Visibility.Visible;
                SetStatus($"已保存 {System.IO.Path.GetFileName(targetPath)}");
            }
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
            save.IsEnabled = quickSave.IsEnabled = copyFile.IsEnabled = true;
            cancel.Visibility = Visibility.Collapsed;
        }
        if (succeeded && !copyToClipboard && closeAfterExport.IsChecked == true)
        {
            forceClose = true;
            Close();
        }
        return succeeded;
    }

    private void OpenSavedFolder()
    {
        if (string.IsNullOrWhiteSpace(lastSavedPath) || !File.Exists(lastSavedPath))
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{lastSavedPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            SetStatus("无法打开文件所在位置。", true);
        }
    }

    private void SetStatus(string message, bool warning = false)
    {
        status.Text = message;
        status.SetResourceReference(TextBlock.ForegroundProperty, warning ? "Danger" : "Muted");
        statusDot.SetResourceReference(Shape.FillProperty, warning ? "Danger" : saved ? "Positive" : "Accent");
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
