using System.ComponentModel;
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

internal sealed class RecordingWindow : Window
{
    private readonly PixelRect area;
    private readonly string sessionRoot;
    private readonly Action<RecordedClip> showPreview;
    private readonly TextBlock elapsed = new(), status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse headerDot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Ellipse statusDot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
    private readonly StackPanel options = new() { Orientation = Orientation.Vertical };
    private readonly ComboBox fps = new(), delay = new(), mode = new();
    private readonly CheckBox cursor = new() { Content = "显示光标", IsChecked = true };
    private readonly CheckBox system = new() { Content = "系统声音" }, microphone = new() { Content = "麦克风" };
    private readonly CheckBox mouseCues = new() { Content = "鼠标点击和滚轮" }, keyCues = new() { Content = "显示快捷键" },
        camera = new() { Content = "摄像头" }, circle = new() { Content = "圆形" },
        cameraMirror = new() { Content = "镜像", IsChecked = true },
        hardwareAccel = new() { Content = "硬件加速" },
        quickAutoSave = new() { Content = "直接存至视频目录" };
    private readonly ComboBox device = new() { MinWidth = 115, MaxWidth = 180 }, corner = new(), cameraSize = new(), quickQuality = new();
    private readonly ComboBox microphoneDevice = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Border micHost, camHost, quickHost, annotateBar;
    private readonly WrapPanel cameraControls = new(), quickControls = new();
    private readonly Button start = new() { Content = "开始录制" }, pause = new() { Content = "暂停" },
        redo = new() { Content = "重录" }, annotate = new() { Content = "标注" }, cancel = new() { Content = "放弃" };
    private readonly Button undoAnnotate = new() { Content = "撤销" }, clearAnnotate = new() { Content = "清空" },
        passAnnotate = new() { Content = "穿透" };
    private readonly List<(LiveAnnotationTool Tool, Button Button)> toolButtons = [];
    private readonly List<(Color Color, Button Button, Ellipse Swatch)> swatchButtons = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? countdown;
    private RegionRecording? recording;
    internal RecordingSession? ActiveSession => recording?.Session;
    private readonly RecordingRegionFrame regionFrame;
    private readonly RecordingAnnotationOverlay annotationOverlay;
    internal RecordingRegionFrame RegionFrame => regionFrame;
    internal RecordingAnnotationOverlay AnnotationOverlay => annotationOverlay;
    private RecordingInput? inputCapture;
    private bool paused, abandoned, restarted, closing, forceClose, placeOk, stopping, micaEnabled;

    public RecordingWindow(PixelRect area, string sessionRoot, Action<RecordedClip> showPreview)
    {
        this.area = area;
        this.sessionRoot = sessionRoot;
        this.showPreview = showPreview;
        regionFrame = new RecordingRegionFrame(area);
        annotationOverlay = new RecordingAnnotationOverlay(area);
        Title = "YXRL 屏幕录制";
        if (Theme.AppIcon is { } appIcon) Icon = appIcon;
        AutomationProperties.SetAutomationId(this, "YxrlRecorder");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        Width = 392;
        SizeToContent = SizeToContent.Height;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Ink");

        var column = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        Content = column;

        // Header card: live clock + region size badge
        elapsed.FontSize = 20;
        elapsed.FontWeight = FontWeights.SemiBold;
        elapsed.VerticalAlignment = VerticalAlignment.Center;
        elapsed.Text = "00:00";
        Typography.SetNumeralAlignment(elapsed, FontNumeralAlignment.Tabular);

        var sizeText = new TextBlock
        {
            Text = $"{area.Width} × {area.Height} px",
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };
        sizeText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Typography.SetNumeralAlignment(sizeText, FontNumeralAlignment.Tabular);
        var sizePill = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = sizeText
        };
        sizePill.SetResourceReference(Border.BackgroundProperty, "Card");
        sizePill.SetResourceReference(Border.BorderBrushProperty, "Line");

        var headerDock = new DockPanel();
        DockPanel.SetDock(sizePill, Dock.Right);
        headerDock.Children.Add(sizePill);
        var clockGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        clockGroup.Children.Add(headerDot);
        clockGroup.Children.Add(elapsed);
        headerDock.Children.Add(clockGroup);

        var headerCard = new Border { Margin = new Thickness(0, 0, 0, 10), Child = headerDock };
        headerCard.SetResourceReference(StyleProperty, "GroupCard");
        headerCard.Padding = new Thickness(12, 9, 12, 9);
        column.Children.Add(headerCard);

        // Options card
        foreach (int value in new[] { 5, 16, 24, 30, 60 }) fps.Items.Add(value);
        foreach (int value in new[] { 0, 1, 2, 3, 5, 10 }) delay.Items.Add(value);
        mode.Items.Add("普通录制");
        mode.Items.Add("快速保存 MP4");
        fps.SelectedItem = 24;
        delay.SelectedItem = 0;
        mode.SelectedIndex = 0;

        var fields = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        var fieldDefs = new[] { ("帧率", fps), ("延时（秒）", delay), ("模式", mode) };
        for (int i = 0; i < fieldDefs.Length; i++)
        {
            var (label, menu) = fieldDefs[i];
            AutomationProperties.SetName(menu, label);
            var field = new StackPanel { Margin = new Thickness(0, 0, i < 2 ? 8 : 0, 0) };
            var lbl = new TextBlock { Text = label, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 4) };
            lbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            field.Children.Add(lbl);
            menu.MinHeight = 30;
            menu.Padding = new Thickness(8, 3, 24, 3);
            field.Children.Add(menu);
            Grid.SetColumn(field, i);
            fields.Children.Add(field);
        }
        options.Children.Add(fields);

        var optSep = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 8) };
        optSep.SetResourceReference(Border.BackgroundProperty, "Line");
        options.Children.Add(optSep);

        var inputs = new UniformGrid { Columns = 2 };
        foreach (var control in new[] { cursor, system, microphone, mouseCues, keyCues, camera, hardwareAccel })
        {
            control.Margin = new Thickness(0, 3, 8, 3);
            inputs.Children.Add(control);
        }
        options.Children.Add(inputs);

        AutomationProperties.SetName(microphoneDevice, "麦克风设备");
        microphoneDevice.ToolTip = "麦克风设备";
        var micStack = new StackPanel();
        var micLbl = new TextBlock { Text = "麦克风输入", FontSize = 11.5, Margin = new Thickness(0, 0, 0, 4) };
        micLbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        micStack.Children.Add(micLbl);
        micStack.Children.Add(microphoneDevice);
        micHost = SubCard(micStack, Visibility.Collapsed);
        microphone.Checked += (_, _) => ChooseMicrophone();
        microphone.Unchecked += (_, _) => micHost.Visibility = Visibility.Collapsed;
        options.Children.Add(micHost);

        foreach (string value in new[] { "右下", "左下", "右上", "左上" }) corner.Items.Add(value);
        corner.SelectedIndex = 0;
        foreach (string value in new[] { "小", "中", "大" }) cameraSize.Items.Add(value);
        cameraSize.SelectedIndex = 1;
        foreach (var (control, name) in new (Control, string)[] {
            (device, "摄像头设备"), (corner, "摄像头位置"), (cameraSize, "摄像头大小"),
            (circle, "圆形摄像头"), (cameraMirror, "摄像头镜像") })
        {
            AutomationProperties.SetName(control, name);
            control.Margin = new Thickness(0, 2, 8, 2);
            cameraControls.Children.Add(control);
        }
        camHost = SubCard(cameraControls, Visibility.Collapsed);
        camera.Checked += async (_, _) => await ChooseCamera();
        camera.Unchecked += async (_, _) => await ChooseCamera();
        options.Children.Add(camHost);

        foreach (string value in new[] { "轻量", "标准", "高清" }) quickQuality.Items.Add(value);
        quickQuality.SelectedIndex = 1;
        AutomationProperties.SetName(quickQuality, "快速录制画质");
        var quickLbl = new TextBlock { Text = "导出画质", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        quickLbl.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        quickControls.Children.Add(quickLbl);
        quickQuality.Margin = new Thickness(0, 0, 12, 0);
        quickControls.Children.Add(quickQuality);
        quickAutoSave.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(quickAutoSave, "直接存至视频目录");
        quickControls.Children.Add(quickAutoSave);
        quickHost = SubCard(quickControls, Visibility.Collapsed);
        mode.SelectionChanged += (_, _) => quickHost.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        options.Children.Add(quickHost);

        var optionsCard = new Border { Margin = new Thickness(0, 0, 0, 10), Child = options };
        optionsCard.SetResourceReference(StyleProperty, "GroupCard");
        optionsCard.Padding = new Thickness(12, 10, 12, 10);
        column.Children.Add(optionsCard);

        // Action strip & live annotation bar
        var actions = new DockPanel();
        var actionLeft = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        start.SetResourceReference(StyleProperty, "PrimaryButton");
        pause.SetResourceReference(StyleProperty, "SecondaryButton");
        redo.SetResourceReference(StyleProperty, "SecondaryButton");
        annotate.SetResourceReference(StyleProperty, "SecondaryButton");
        cancel.SetResourceReference(StyleProperty, "SecondaryButton");
        AutomationProperties.SetName(redo, "重新录制");
        AutomationProperties.SetName(annotate, "边录边标");

        foreach (var button in new[] { start, pause, annotate, redo, cancel })
        {
            button.MinHeight = 30;
            button.Padding = new Thickness(11, 4, 11, 4);
            button.Margin = new Thickness(0, 0, 7, 0);
            actionLeft.Children.Add(button);
        }
        pause.Visibility = annotate.Visibility = redo.Visibility = cancel.Visibility = Visibility.Collapsed;
        start.Click += async (_, _) => await StartOrStop();
        pause.Click += (_, _) => TogglePause();
        redo.Click += (_, _) => RestartRecording();
        annotate.Click += (_, _) => SetLiveAnnotationMode(!annotationOverlay.IsDrawing);
        cancel.Click += (_, _) => Close();
        annotationOverlay.DrawingModeChanged += drawing => Dispatcher.Invoke(() =>
        {
            if (closing) return;
            SyncAnnotationToggleVisual(drawing);
        });
        actions.Children.Add(actionLeft);
        column.Children.Add(actions);

        var annotateRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (tool, label) in new[] {
            (LiveAnnotationTool.Pen, "画笔"),
            (LiveAnnotationTool.Arrow, "箭头"),
            (LiveAnnotationTool.Rectangle, "矩形"),
            (LiveAnnotationTool.Number, "序号") })
        {
            var btn = new Button
            {
                Content = label,
                MinHeight = 26,
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 12,
                Margin = new Thickness(0, 2, 5, 2)
            };
            btn.SetResourceReference(StyleProperty, "SecondaryButton");
            AutomationProperties.SetName(btn, "标注" + label);
            btn.Click += (_, _) => SelectAnnotationTool(tool);
            toolButtons.Add((tool, btn));
            annotateRow.Children.Add(btn);
        }
        var swatchSep = new Border { Width = 1, Height = 16, Margin = new Thickness(3, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        swatchSep.SetResourceReference(Border.BackgroundProperty, "Line");
        annotateRow.Children.Add(swatchSep);
        foreach (var color in RecordingAnnotationOverlay.Palette)
        {
            var dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(color) };
            var btn = new Button
            {
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 2, 4, 2),
                Content = dot
            };
            btn.SetResourceReference(StyleProperty, "SecondaryButton");
            btn.Click += (_, _) => SelectAnnotationColor(color);
            swatchButtons.Add((color, btn, dot));
            annotateRow.Children.Add(btn);
        }
        foreach (var (btn, name) in new[] { (undoAnnotate, "撤销标注"), (clearAnnotate, "清空标注"), (passAnnotate, "切回穿透") })
        {
            btn.SetResourceReference(StyleProperty, "SecondaryButton");
            btn.MinHeight = 26;
            btn.Padding = new Thickness(8, 2, 8, 2);
            btn.FontSize = 12;
            btn.Margin = new Thickness(3, 2, 0, 2);
            AutomationProperties.SetName(btn, name);
            annotateRow.Children.Add(btn);
        }
        undoAnnotate.Click += (_, _) => annotationOverlay.Undo();
        clearAnnotate.Click += (_, _) => annotationOverlay.Clear();
        passAnnotate.Click += (_, _) => SetLiveAnnotationMode(false);
        annotateBar = SubCard(annotateRow, Visibility.Collapsed);
        column.Children.Add(annotateBar);
        SelectAnnotationTool(LiveAnnotationTool.Pen);
        SelectAnnotationColor(RecordingAnnotationOverlay.Palette[0]);

        var statusRow = new DockPanel { Margin = new Thickness(2, 9, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(statusDot, Dock.Left);
        statusRow.Children.Add(statusDot);
        statusRow.Children.Add(status);
        column.Children.Add(statusRow);

        SetStatus("就绪");
        timer.Tick += (_, _) => elapsed.Text = TimeLabel(recording?.Elapsed ?? TimeSpan.Zero);
        SourceInitialized += (_, _) =>
        {
            ApplyChrome();
            regionFrame.Show();
        };
        ContentRendered += (_, _) => PositionControls();
        SizeChanged += (_, _) => { if (IsLoaded) PositionControls(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || mode.IsDropDownOpen || fps.IsDropDownOpen || delay.IsDropDownOpen ||
                device.IsDropDownOpen || corner.IsDropDownOpen || cameraSize.IsDropDownOpen ||
                quickQuality.IsDropDownOpen || microphoneDevice.IsDropDownOpen)
                return;
            e.Handled = true;
            Close();
        };
        Closing += ConfirmClose;
        Closed += (_, _) =>
        {
            timer.Stop();
            inputCapture?.Dispose();
            annotationOverlay.CloseSafely();
            regionFrame.CloseSafely();
        };
    }

    internal void RefreshTheme() => ApplyChrome();

    private void ApplyChrome()
    {
        micaEnabled = Theme.ApplyWindowChrome(this, Native.BackdropKind.Mica);
        if (micaEnabled) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "Surface");
    }

    private static Border SubCard(UIElement child, Visibility initial)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = initial,
            Child = child
        };
        border.SetResourceReference(Border.BackgroundProperty, "Card");
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }

    private void ChooseMicrophone()
    {
        var previous = (microphoneDevice.SelectedItem as RecordingMicrophone)?.Id;
        try { microphoneDevice.ItemsSource = RecordingAudio.Microphones(); }
        catch (ExternalException) { microphoneDevice.ItemsSource = new[] { new RecordingMicrophone(null, "Windows 默认麦克风") }; }
        microphoneDevice.SelectedItem = microphoneDevice.Items.OfType<RecordingMicrophone>().FirstOrDefault(item => item.Id == previous)
            ?? microphoneDevice.Items[0];
        micHost.Visibility = Visibility.Visible;
    }

    private async Task ChooseCamera()
    {
        camHost.Visibility = camera.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
            if (closing || Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (closing || camera.IsChecked != true) return;
                device.ItemsSource = available;
                var preferred = available.FirstOrDefault(item => !item.Name.Contains("IR Camera", StringComparison.OrdinalIgnoreCase));
                device.SelectedItem = preferred ?? available.FirstOrDefault();
                SetStatus(available.Length > 0 ? "就绪" : "没有检测到摄像头。", available.Length == 0);
            });
        }
        catch (TaskCanceledException) when (closing || Dispatcher.HasShutdownStarted)
        {
        }
        catch (Exception ex) when (ex is ExternalException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            if (!closing && !Dispatcher.HasShutdownStarted)
            {
                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!closing) SetStatus("无法读取摄像头列表。 " + ex.Message, true);
                    });
                }
                catch (TaskCanceledException) { }
            }
        }
        finally
        {
            if (!closing && !Dispatcher.HasShutdownStarted)
            {
                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!closing) start.IsEnabled = true;
                    });
                }
                catch (TaskCanceledException) { }
            }
        }
    }

    private void PositionControls()
    {
        placeOk = CaptureWindowPlacement.Place(this, area);
        if (!placeOk) SetStatus("当前选区无法避开录制控制条。请缩小选区。", true);
    }

    internal static string TimeLabel(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}" : value.ToString(@"mm\:ss");

    private void SetStatus(string message, bool warning = false, string? stateDotKey = null)
    {
        status.Text = message;
        status.SetResourceReference(TextBlock.ForegroundProperty, warning ? "Danger" : "Muted");
        string dotKey = stateDotKey ?? (warning ? "Danger" : "Accent");
        statusDot.SetResourceReference(Shape.FillProperty, dotKey);
        headerDot.SetResourceReference(Shape.FillProperty, stateDotKey ?? (warning ? "Warning" : "Accent"));
    }

    private void SetLiveAnnotationMode(bool drawing)
    {
        if (closing) return;
        annotationOverlay.SetDrawingMode(drawing);
        SyncAnnotationToggleVisual(annotationOverlay.IsDrawing);
    }

    private void SyncAnnotationToggleVisual(bool drawing)
    {
        annotateBar.Visibility = drawing ? Visibility.Visible : Visibility.Collapsed;
        annotate.SetResourceReference(Control.BackgroundProperty, drawing ? "Hot" : "Paper");
        annotate.SetResourceReference(Control.BorderBrushProperty, drawing ? "Accent" : "PanelBorder");
        annotate.SetResourceReference(Control.ForegroundProperty, drawing ? "Accent" : "Ink");
        if (recording is not null)
        {
            SetStatus(
                drawing ? "● 标注绘制中（右键或 Esc 切回操作）" : (paused ? "已暂停" : "● 正在录制"),
                warning: !drawing && !paused,
                stateDotKey: drawing ? "Accent" : (paused ? "Warning" : "DangerFill"));
        }
    }

    private void SelectAnnotationTool(LiveAnnotationTool selected)
    {
        annotationOverlay.CurrentTool = selected;
        foreach (var (tool, btn) in toolButtons)
        {
            bool active = tool == selected;
            btn.SetResourceReference(Control.BackgroundProperty, active ? "Hot" : "Paper");
            btn.SetResourceReference(Control.BorderBrushProperty, active ? "Accent" : "PanelBorder");
            btn.SetResourceReference(Control.ForegroundProperty, active ? "Accent" : "Ink");
        }
    }

    private void SelectAnnotationColor(Color selected)
    {
        annotationOverlay.CurrentColor = selected;
        foreach (var (color, btn, _) in swatchButtons)
        {
            bool active = color == selected;
            btn.SetResourceReference(Control.BackgroundProperty, active ? "Hot" : "Paper");
            btn.SetResourceReference(Control.BorderBrushProperty, active ? "Accent" : "PanelBorder");
        }
    }

    private void RestartRecording()
    {
        if (recording is not { } running || stopping)
            return;
        restarted = true;
        stopping = true;
        start.IsEnabled = pause.IsEnabled = redo.IsEnabled = annotate.IsEnabled = cancel.IsEnabled = false;
        SetStatus("正在重置录制…");
        annotationOverlay.Clear();
        SyncAnnotationToggleVisual(false);
        running.Stop(abandon: true);
    }

    internal static string UniqueQuickSavePath(string extension = "mp4", string? customRoot = null)
    {
        string baseDir = customRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string dir = System.IO.Path.Combine(baseDir, "YXRL");
        Directory.CreateDirectory(dir);
        string stem = $"YXRL-{DateTime.Now:yyyyMMdd-HHmmss}";
        string candidate = System.IO.Path.Combine(dir, $"{stem}.{extension}");
        for (int n = 1; File.Exists(candidate); n++)
            candidate = System.IO.Path.Combine(dir, $"{stem}_{n}.{extension}");
        return candidate;
    }

    private Task StartOrStop() => StartOrStop(null);

    private async Task StartOrStop(string? presetTarget)
    {
        if (recording is { } running)
        {
            stopping = true;
            start.IsEnabled = pause.IsEnabled = redo.IsEnabled = annotate.IsEnabled = cancel.IsEnabled = false;
            annotationOverlay.SetDrawingMode(false);
            SetStatus("正在完成录制…");
            running.Stop();
            return;
        }

        string? target = presetTarget;
        var selectedQuality = mode.SelectedIndex == 1 ? (RecordingQuality)Math.Clamp(quickQuality.SelectedIndex, 0, 2) : RecordingQuality.Standard;
        if (mode.SelectedIndex == 1 && target is null)
        {
            if (quickAutoSave.IsChecked == true)
            {
                target = UniqueQuickSavePath("mp4");
            }
            else
            {
                var save = new SaveFileDialog { Title = "保存录屏", Filter = "MP4 视频 (*.mp4)|*.mp4", DefaultExt = ".mp4",
                    FileName = $"YXRL-{DateTime.Now:yyyyMMdd-HHmmss}.mp4", AddExtension = true };
                if (save.ShowDialog(this) != true)
                    return;
                target = save.FileName;
            }
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
            cameraOptions = new(selectedDevice, (CameraCorner)corner.SelectedIndex, circle.IsChecked == true,
                cameraMirror.IsChecked == true, (CameraSize)Math.Clamp(cameraSize.SelectedIndex, 0, 2));
        }
        bool hwAccel = hardwareAccel.IsChecked == true;
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
                SetStatus($"{seconds} 秒后开始录制", stateDotKey: "Warning");
                await Task.Delay(TimeSpan.FromSeconds(1), session.Token);
            }
            session.Token.ThrowIfCancellationRequested();
            regionFrame.UpdateState(RecordingFrameState.Live);
            var preferences = new RecordingOptions((int)fps.SelectedItem, cursor.IsChecked == true,
                system.IsChecked == true, microphone.IsChecked == true,
                mouseCues.IsChecked == true, keyCues.IsChecked == true, cameraOptions,
                (microphoneDevice.SelectedItem as RecordingMicrophone)?.Id, selectedQuality, hwAccel);
            var newRecording = new RegionRecording(area, preferences, sessionRoot);
            recording = newRecording;
            if (preferences.MouseCues || preferences.KeyCues)
                inputCapture = new RecordingInput(area, new WindowInteropHelper(this).Handle, preferences.MouseCues, preferences.KeyCues,
                    newRecording.CueTime, newRecording.AddCue);
            start.SetResourceReference(StyleProperty, "DangerButton");
            start.Content = "停止录制";
            start.IsEnabled = pause.IsEnabled = redo.IsEnabled = annotate.IsEnabled = true;
            pause.Visibility = annotate.Visibility = redo.Visibility = Visibility.Visible;
            SetStatus("● 正在录制", warning: true, stateDotKey: "DangerFill");
            timer.Start();
            var result = await recording.Completion;
            if (restarted || abandoned || closing)
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
                    var quickEdits = RecordingEdits.Whole(result) with { Quality = selectedQuality, HardwareAcceleration = hwAccel };
                    await Task.Run(() => RecordingExport.Save(result, target, RecordingFormat.Mp4, TimeSpan.Zero,
                        result.Duration, session.Token, edits: quickEdits));
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
            bool shouldRestart = restarted && !closing;
            countdown = null;
            session.Dispose();
            inputCapture?.Dispose();
            inputCapture = null;
            recording = null;
            timer.Stop();
            annotationOverlay.Clear();
            annotationOverlay.SetDrawingMode(false);
            if (!closing)
            {
                regionFrame.UpdateState(RecordingFrameState.Ready);
                elapsed.Text = "00:00";
                options.IsEnabled = start.IsEnabled = pause.IsEnabled = redo.IsEnabled = annotate.IsEnabled = cancel.IsEnabled = true;
                start.SetResourceReference(StyleProperty, "PrimaryButton");
                start.Content = "开始录制";
                pause.Content = "暂停";
                pause.Visibility = annotate.Visibility = redo.Visibility = cancel.Visibility = annotateBar.Visibility = Visibility.Collapsed;
                SyncAnnotationToggleVisual(false);
                paused = stopping = abandoned = restarted = false;
                if (shouldRestart)
                {
                    string? nextTarget = mode.SelectedIndex == 1 && quickAutoSave.IsChecked != true ? target : null;
                    _ = Dispatcher.BeginInvoke(async () => { if (!closing) await StartOrStop(nextTarget); });
                }
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
        SyncAnnotationToggleVisual(annotationOverlay.IsDrawing);
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
        annotationOverlay.Clear();
        annotationOverlay.SetDrawingMode(false);
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
        annotationOverlay.CloseSafely();
        regionFrame.CloseSafely();
        Close();
    }

}
