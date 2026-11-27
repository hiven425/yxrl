using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed class RecordingWindow : Window
{
    private readonly PixelRect area;
    private readonly string sessionRoot;
    private readonly Action<RecordedClip> showPreview;
    private readonly TextBlock elapsed = new(), status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel options = new() { Orientation = Orientation.Vertical };
    private readonly ComboBox fps = new(), delay = new(), mode = new();
    private readonly CheckBox cursor = new() { Content = "显示光标", IsChecked = true };
    private readonly CheckBox system = new() { Content = "系统声音" }, microphone = new() { Content = "麦克风" };
    private readonly CheckBox mouseCues = new() { Content = "鼠标点击和滚轮" }, keyCues = new() { Content = "显示快捷键" },
        camera = new() { Content = "摄像头" }, circle = new() { Content = "圆形" };
    private readonly ComboBox device = new() { MinWidth = 120, MaxWidth = 195 }, corner = new();
    private readonly ComboBox microphoneDevice = new() { MinWidth = 120, MaxWidth = 270, HorizontalAlignment = HorizontalAlignment.Left,
        Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    private readonly WrapPanel cameraControls = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 9) };
    private readonly Button start = new() { Content = "开始录制" }, pause = new() { Content = "暂停" };
    private readonly Button cancel = new() { Content = "放弃" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? countdown;
    private RegionRecording? recording;
    internal RecordingSession? ActiveSession => recording?.Session;
    private readonly RecordingRegionFrame regionFrame;
    internal RecordingRegionFrame RegionFrame => regionFrame;
    private RecordingInput? inputCapture;
    private bool paused, abandoned, closing, forceClose, placeOk, stopping;

    public RecordingWindow(PixelRect area, string sessionRoot, Action<RecordedClip> showPreview)
    {
        this.area = area;
        this.sessionRoot = sessionRoot;
        this.showPreview = showPreview;
        regionFrame = new RecordingRegionFrame(area);
        Title = "YXRL 屏幕录制";
        if (Views.Theme.AppIcon is { } appIcon) Icon = appIcon;
        AutomationProperties.SetAutomationId(this, "YxrlRecorder");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        Width = 362;
        SizeToContent = SizeToContent.Height;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Ink");
        var column = new StackPanel { Margin = new Thickness(14) };
        Content = column;
        elapsed.FontSize = 19;
        elapsed.Text = $"{area.Width} × {area.Height} px   ·   00:00";
        column.Children.Add(elapsed);
        column.Children.Add(options);
        foreach (int value in new[] { 5, 16, 24, 30, 60 }) fps.Items.Add(value);
        foreach (int value in new[] { 0, 1, 2, 3, 5, 10 }) delay.Items.Add(value);
        mode.Items.Add("普通录制");
        mode.Items.Add("快速保存 MP4");
        fps.SelectedItem = 24;
        delay.SelectedItem = 0;
        mode.SelectedIndex = 0;
        var fields = new WrapPanel { Margin = new Thickness(0, 9, 0, 5) };
        foreach (var (label, menu) in new[] { ("帧率", fps), ("延时（秒）", delay), ("模式", mode) })
        {
            AutomationProperties.SetName(menu, label);
            var field = new StackPanel { Margin = new Thickness(0, 0, 9, 0) };
            field.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            menu.MinWidth = 53;
            field.Children.Add(menu);
            fields.Children.Add(field);
        }
        options.Children.Add(fields);
        var inputs = new WrapPanel { Margin = new Thickness(0, 5, 0, 10) };
        foreach (var control in new[] { cursor, system, microphone, mouseCues, keyCues, camera })
        {
            control.Margin = new Thickness(0, 2, 13, 2);
            inputs.Children.Add(control);
        }
        options.Children.Add(inputs);
        AutomationProperties.SetName(microphoneDevice, "麦克风设备");
        microphoneDevice.ToolTip = "麦克风设备";
        microphone.Checked += (_, _) => ChooseMicrophone();
        microphone.Unchecked += (_, _) => microphoneDevice.Visibility = Visibility.Collapsed;
        options.Children.Add(microphoneDevice);
        foreach (string value in new[] { "右下", "左下", "右上", "左上" }) corner.Items.Add(value);
        corner.SelectedIndex = 0;
        foreach (var (control, name) in new (Control, string)[] { (device, "摄像头设备"), (corner, "摄像头位置"), (circle, "圆形摄像头") })
        {
            AutomationProperties.SetName(control, name);
            control.Margin = new Thickness(0, 0, 8, 0);
            cameraControls.Children.Add(control);
        }
        camera.Checked += async (_, _) => await ChooseCamera();
        camera.Unchecked += async (_, _) => await ChooseCamera();
        options.Children.Add(cameraControls);
        var actions = new WrapPanel();
        column.Children.Add(actions);
        start.SetResourceReference(StyleProperty, "PrimaryButton");
        foreach (var button in new[] { start, pause, cancel })
        {
            button.MinHeight = 30;
            button.Padding = new Thickness(10, 2, 10, 2);
            button.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(button);
        }
        pause.Visibility = Visibility.Collapsed;
        cancel.Visibility = Visibility.Collapsed;
        start.Click += async (_, _) => await StartOrStop();
        pause.Click += (_, _) => TogglePause();
        cancel.Click += (_, _) => Close();
        status.Margin = new Thickness(0, 9, 0, 0);
        column.Children.Add(status);
        SetStatus("就绪");
        timer.Tick += (_, _) => elapsed.Text = $"{area.Width} × {area.Height} px   ·   {TimeLabel(recording?.Elapsed ?? TimeSpan.Zero)}";
        SourceInitialized += (_, _) => regionFrame.Show();
        ContentRendered += (_, _) => PositionControls();
        SizeChanged += (_, _) => { if (IsLoaded) PositionControls(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || mode.IsDropDownOpen || fps.IsDropDownOpen || delay.IsDropDownOpen ||
                device.IsDropDownOpen || corner.IsDropDownOpen || microphoneDevice.IsDropDownOpen)
                return;
            e.Handled = true;
            Close();
        };
        Closing += ConfirmClose;
        Closed += (_, _) =>
        {
            timer.Stop();
            inputCapture?.Dispose();
            regionFrame.CloseSafely();
        };
    }

    private void ChooseMicrophone()
    {
        var previous = (microphoneDevice.SelectedItem as RecordingMicrophone)?.Id;
        try { microphoneDevice.ItemsSource = RecordingAudio.Microphones(); }
        catch (ExternalException) { microphoneDevice.ItemsSource = new[] { new RecordingMicrophone(null, "Windows 默认麦克风") }; }
        microphoneDevice.SelectedItem = microphoneDevice.Items.OfType<RecordingMicrophone>().FirstOrDefault(item => item.Id == previous)
            ?? microphoneDevice.Items[0];
        microphoneDevice.Visibility = Visibility.Visible;
    }

    private async Task ChooseCamera()
    {
        cameraControls.Visibility = camera.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (camera.IsChecked != true)
        {
            SetStatus("就绪");
            return;
        }
        start.IsEnabled = false;
        SetStatus("正在读取摄像头设备…");
        try
        {
            var available = await Task.Run(RecordingCamera.List);
            if (closing || camera.IsChecked != true) return;
            device.ItemsSource = available;
            var preferred = available.FirstOrDefault(item => !item.Name.Contains("IR Camera", StringComparison.OrdinalIgnoreCase));
            device.SelectedItem = preferred ?? available.FirstOrDefault();
            SetStatus(available.Length > 0 ? "就绪" : "没有检测到摄像头。", available.Length == 0);
        }
        catch (Exception ex) when (ex is ExternalException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            if (!closing)
                SetStatus("无法读取摄像头列表。 " + ex.Message, true);
        }
        finally { if (!closing) start.IsEnabled = true; }
    }

    private void PositionControls()
    {
        placeOk = CaptureWindowPlacement.Place(this, area);
        if (!placeOk) SetStatus("当前选区无法避开录制控制条。请缩小选区。", true);
    }

    internal static string TimeLabel(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}" : value.ToString(@"mm\:ss");

    private void SetStatus(string message, bool warning = false)
    {
        status.Text = message;
        status.SetResourceReference(TextBlock.ForegroundProperty, warning ? "Danger" : "Muted");
    }

    private async Task StartOrStop()
    {
        if (recording is { } running)
        {
            stopping = true;
            start.IsEnabled = pause.IsEnabled = cancel.IsEnabled = false;
            SetStatus("正在完成录制…");
            running.Stop();
            return;
        }

        string? target = null;
        if (mode.SelectedIndex == 1)
        {
            var save = new SaveFileDialog { Title = "保存录屏", Filter = "MP4 视频 (*.mp4)|*.mp4", DefaultExt = ".mp4",
                FileName = $"YXRL-{DateTime.Now:yyyyMMdd-HHmmss}.mp4", AddExtension = true };
            if (save.ShowDialog(this) != true)
                return;
            target = save.FileName;
        }
        if (!placeOk)
            return;
        CameraOptions? cameraOptions = null;
        if (camera.IsChecked == true)
        {
            if (device.SelectedItem is not CameraDevice selectedDevice)
            {
                SetStatus("请选择可用的摄像头，或关闭摄像头后录制。", true);
                return;
            }
            cameraOptions = new(selectedDevice, (CameraCorner)corner.SelectedIndex, circle.IsChecked == true);
        }
        var session = new CancellationTokenSource();
        countdown = session;
        options.IsEnabled = start.IsEnabled = false;
        cancel.Visibility = Visibility.Visible;
        try
        {
            if (!RegionRecording.EnsureDisplay())
                throw new InvalidOperationException("Windows 没有活动显示器。请点亮屏幕后重新录制。");
            for (int seconds = (int)delay.SelectedItem; seconds > 0; seconds--)
            {
                regionFrame.UpdateState(RecordingFrameState.Countdown, seconds);
                SetStatus($"{seconds} 秒后开始录制");
                await Task.Delay(TimeSpan.FromSeconds(1), session.Token);
            }
            session.Token.ThrowIfCancellationRequested();
            regionFrame.UpdateState(RecordingFrameState.Live);
            var preferences = new RecordingOptions((int)fps.SelectedItem, cursor.IsChecked == true,
                system.IsChecked == true, microphone.IsChecked == true,
                mouseCues.IsChecked == true, keyCues.IsChecked == true, cameraOptions, (microphoneDevice.SelectedItem as RecordingMicrophone)?.Id);
            var newRecording = new RegionRecording(area, preferences, sessionRoot);
            recording = newRecording;
            if (preferences.MouseCues || preferences.KeyCues)
                inputCapture = new RecordingInput(area, new WindowInteropHelper(this).Handle, preferences.MouseCues, preferences.KeyCues,
                    newRecording.CueTime, newRecording.AddCue);
            start.Content = "停止录制";
            start.IsEnabled = pause.IsEnabled = true;
            pause.Visibility = Visibility.Visible;
            SetStatus("● 正在录制", true);
            timer.Start();
            var result = await recording.Completion;
            if (abandoned || closing)
                return;
            if (result is null)
            {
                SetStatus("录制没有产生画面，请延长录制时间再试。", true);
                return;
            }
            if (target is not null)
            {
                try
                {
                    await Task.Run(() => RecordingExport.Save(result, target, RecordingFormat.Mp4, TimeSpan.Zero,
                        result.Duration, session.Token));
                    result.Session?.Delete();
                    CloseImmediately();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or InvalidOperationException)
                {
                    showPreview(result);
                    MessageBox.Show($"无法保存到指定位置。可在回放中重新保存。\n{ex.Message}", "YXRL 屏幕录制", MessageBoxButton.OK, MessageBoxImage.Information);
                    CloseImmediately();
                }
            }
            else
            {
                showPreview(result);
                CloseImmediately();
            }
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ExternalException
            or DllNotFoundException or EntryPointNotFoundException)
        {
            if (!closing)
            {
                if (recording is { } failed && !failed.Completion.IsCompleted) failed.Stop(abandon: true);
                var recover = recording?.Session.Parts().Any() == true ? " 已封存片段可从「恢复录屏」打开。" : "";
                SetStatus("录制失败。 " + ex.Message + recover, true);
            }
        }
        finally
        {
            countdown = null;
            session.Dispose();
            inputCapture?.Dispose();
            inputCapture = null;
            recording = null;
            timer.Stop();
            if (!closing)
            {
                regionFrame.UpdateState(RecordingFrameState.Ready);
                options.IsEnabled = start.IsEnabled = cancel.IsEnabled = true;
                start.Content = "开始录制";
                pause.Content = "暂停";
                pause.Visibility = cancel.Visibility = Visibility.Collapsed;
                paused = stopping = abandoned = false;
            }
        }
    }

    private void TogglePause()
    {
        if (recording is null)
            return;
        paused = !paused;
        recording.Pause(paused);
        regionFrame.UpdateState(paused ? RecordingFrameState.Paused : RecordingFrameState.Live);
        pause.Content = paused ? "继续" : "暂停";
        SetStatus(paused ? "已暂停" : "● 正在录制", !paused);
    }

    private void ConfirmClose(object? sender, CancelEventArgs e)
    {
        if (forceClose)
        {
            closing = true;
            return;
        }
        if (stopping)
        {
            e.Cancel = true;
            return;
        }
        if (recording is not null && MessageBox.Show(this, "放弃本次录制？", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        Abort();
    }

    public void Abort()
    {
        closing = abandoned = true;
        countdown?.Cancel();
        if (recording is not { } running)
            return;
        running.Stop(abandon: true);
        // Observe completion and remove a finalized clip even if the app dispatcher has shut down.
        _ = running.Completion.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully && task.Result is { } clip)
                clip.Session?.Delete();
            _ = task.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void CloseImmediately()
    {
        forceClose = true;
        Close();
    }

    public void Shutdown()
    {
        forceClose = true;
        closing = true;
        countdown?.Cancel();
        if (recording is { } running)
        {
            running.Stop(interrupt: true);
            try { running.Completion.Wait(TimeSpan.FromSeconds(2)); }
            catch (AggregateException) { } // Earlier sealed parts remain available for recovery.
        }
        regionFrame.CloseSafely();
        Close();
    }

}
