using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Yxrl.Capture;

namespace Yxrl.Views;

internal sealed class CaptureSettingsPage : StackPanel
{
    public const int Section = 3;
    private readonly CheckBox enabled = new() { Content = "启用截图工具", FontSize = 14, FontWeight = FontWeights.SemiBold };
    private readonly TextBox address = new(), model = new(), target = new();
    private readonly PasswordBox password = new();
    private readonly CheckBox clearSecret = new() { Content = "删除此地址已保存的密钥", Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button testButton = new() { Content = "测试连接", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 8, 8, 0) };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock testStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Dictionary<ShotAction, Button> recorders = new();
    private readonly Dictionary<ShotAction, TextBlock> warnings = new();
    private CaptureTools? tools;
    private ShotShortcuts keys = new();
    private ShotAction? recording;
    private CancellationTokenSource? testCancel;

    public CaptureOptions Options => new()
    {
        Enabled = enabled.IsChecked == true,
        Shortcuts = keys
    };
    public TranslationOptions Translation => new(address.Text.Trim(), model.Text.Trim(), target.Text.Trim());
    public string? EditedSecret => clearSecret.IsChecked == true ? "" : password.Password.Length == 0 ? null : password.Password;

    public CaptureSettingsPage()
    {
        // Card 1: Master switch
        var toggleStack = new StackPanel();
        toggleStack.Children.Add(enabled);
        var toggleHint = Muted("控制全局快捷键与月历底栏截图工具开关；全局热键支持截取屏幕、贴图剪贴板及恢复已关闭贴图。");
        toggleHint.Margin = new Thickness(25, 2, 0, 0);
        toggleStack.Children.Add(toggleHint);
        Children.Add(Card(toggleStack, topMargin: 0));

        // Card 2: Shortcuts
        var keyStack = new StackPanel();
        keyStack.Children.Add(SectionTitle("快捷键"));
        foreach (var action in Enum.GetValues<ShotAction>())
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var clear = new Button
            {
                Content = "停用",
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 3, 10, 3),
                MinHeight = 28
            };
            ApplyStyle(clear, "SecondaryButton");
            AutomationProperties.SetName(clear, "停用" + CaptureOptions.Label(action));
            clear.Click += (_, _) =>
            {
                StopRecording();
                keys = keys.Bind(action, "");
                Refresh();
            };
            DockPanel.SetDock(clear, Dock.Right);
            row.Children.Add(clear);

            var button = new Button
            {
                MinWidth = 104,
                Padding = new Thickness(10, 3, 10, 3),
                MinHeight = 28
            };
            ApplyStyle(button, "SecondaryButton");
            AutomationProperties.SetName(button, CaptureOptions.Label(action) + "快捷键");
            recorders.Add(action, button);
            button.Click += (_, _) => StartRecording(action);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);

            var labelPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            labelPanel.Children.Add(new TextBlock { Text = CaptureOptions.Label(action), VerticalAlignment = VerticalAlignment.Center });
            bool isGlobal = action is ShotAction.Start or ShotAction.Clipboard or ShotAction.RestorePin;
            labelPanel.Children.Add(Badge(isGlobal ? "全局" : "工具内", isGlobal));
            row.Children.Add(labelPanel);
            keyStack.Children.Add(row);

            var error = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 1, 0, 4),
                Visibility = Visibility.Collapsed
            };
            error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            warnings[action] = error;
            keyStack.Children.Add(error);
        }

        var restore = new Button
        {
            Content = "恢复默认快捷键",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        ApplyStyle(restore, "SecondaryButton");
        restore.Click += (_, _) =>
        {
            StopRecording();
            keys = new();
            Refresh();
        };
        keyStack.Children.Add(restore);
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        keyStack.Children.Add(feedback);
        Children.Add(Card(keyStack));

        // Card 3: Translation API
        var trStack = new StackPanel();
        trStack.Children.Add(SectionTitle("翻译服务"));
        trStack.Children.Add(Muted("兼容 OpenAI Chat Completions 接口，API 密钥由当前 Windows 用户凭据管理器保管，仅主动点击翻译时发送选定文本。"));
        address.ToolTip = "例如 http://localhost:11434/v1";
        Field(trStack, "API 基础地址", address);
        Field(trStack, "模型名", model);
        Field(trStack, "默认目标语言", target);
        Field(trStack, "API 密钥（可选，留空保留原密钥）", password);
        trStack.Children.Add(clearSecret);
        ApplyStyle(testButton, "SecondaryButton");
        testButton.Click += (_, _) => _ = TestTranslationAsync();
        testStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var testRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        testRow.Children.Add(testButton);
        testRow.Children.Add(testStatus);
        trStack.Children.Add(testRow);
        Children.Add(Card(trStack));

        PreviewKeyDown += CaptureKey;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                StopRecording();
                testCancel?.Cancel();
            }
        };
    }

    private Border Card(UIElement child, double topMargin = 10)
    {
        var border = new Border
        {
            Child = child,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(15, 13, 15, 13),
            Margin = new Thickness(0, topMargin, 0, 0)
        };
        border.SetResourceReference(Border.BackgroundProperty, "Paper");
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }

    private static TextBlock SectionTitle(string title) => new()
    {
        Text = title,
        FontWeight = FontWeights.SemiBold,
        FontSize = 14.5,
        Margin = new Thickness(0, 0, 0, 6)
    };

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return block;
    }

    private static Border Badge(string text, bool accent)
    {
        var label = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.Medium };
        label.SetResourceReference(TextBlock.ForegroundProperty, accent ? "Accent" : "Muted");
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = label
        };
        badge.SetResourceReference(Border.BackgroundProperty, accent ? "Hot" : "Card");
        return badge;
    }

    private void ApplyStyle(Button button, string key)
    {
        if (TryFindResource(key) is Style st)
            button.Style = st;
    }

    private static void Field(StackPanel parent, string label, Control control)
    {
        var title = new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 8, 0, 3) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        parent.Children.Add(title);
        AutomationProperties.SetName(control, label);
        parent.Children.Add(control);
    }

    public void Load(Preferences settings, CaptureTools runtime)
    {
        StopRecording();
        testCancel?.Cancel();
        testStatus.Text = "";
        tools = runtime;
        enabled.IsChecked = settings.Capture.Enabled;
        keys = settings.Capture.Shortcuts;
        address.Text = settings.Translation.BaseUrl;
        model.Text = settings.Translation.Model;
        target.Text = settings.Translation.TargetLanguage;
        password.Clear();
        clearSecret.IsChecked = false;
        feedback.Text = "";
        foreach (var warning in warnings.Values)
            warning.Visibility = Visibility.Collapsed;
        foreach (var error in tools.Hotkeys.Errors)
            ShowConflict(error.Key, error.Value);
        Refresh();
    }

    private async Task TestTranslationAsync()
    {
        if (tools is null) return;
        testCancel?.Cancel();
        testCancel?.Dispose();
        testCancel = new CancellationTokenSource();
        var token = testCancel.Token;
        testButton.IsEnabled = false;
        testStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        testStatus.Text = "正在发送测试请求…";
        try
        {
            var opts = Translation;
            var endpoint = opts.Endpoint() ?? throw new FormatException("请先填写 API 基础地址和模型名。");
            var key = EditedSecret is { Length: > 0 } ? EditedSecret
                : EditedSecret == "" ? null : TranslationCredential.Read(endpoint);
            var reply = await tools.Translator.Translate(opts, "Hello", string.IsNullOrWhiteSpace(opts.TargetLanguage) ? "简体中文" : opts.TargetLanguage, key, token);
            if (!token.IsCancellationRequested)
            {
                testStatus.SetResourceReference(TextBlock.ForegroundProperty, "Positive");
                testStatus.Text = $"连接成功：{reply}";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or OperationCanceledException or IOException or JsonException or Win32Exception or InvalidDataException)
        {
            if (!token.IsCancellationRequested)
            {
                testStatus.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
                testStatus.Text = ex is OperationCanceledException ? "请求超时，请检查服务是否运行。" : ex.Message;
            }
        }
        finally
        {
            testButton.IsEnabled = true;
        }
    }

    public void ShowConflict(ShotAction action, string message)
    {
        warnings[action].Text = message;
        warnings[action].Visibility = Visibility.Visible;
    }

    private void Refresh()
    {
        foreach (var (action, b) in recorders)
            b.Content = recording == action ? (CaptureOptions.AllowsSingleKey(action) ? "请按快捷键" : "请按组合键")
                : keys[action].Length == 0 ? "已停用" : keys[action];
    }

    private void StartRecording(ShotAction action)
    {
        StopRecording();
        recording = action;
        warnings[action].Visibility = Visibility.Collapsed;
        feedback.Text = CaptureOptions.AllowsSingleKey(action) ? "按单键或组合键录入，Esc 取消。" : "按组合键录入，Esc 取消。";
        if (tools is not null)
            tools.Hotkeys.Recording = Accept;
        recorders[action].Focus();
        Refresh();
    }

    public void StopRecording()
    {
        recording = null;
        if (tools is not null)
            tools.Hotkeys.Recording = null;
        Refresh();
    }

    private void Accept(ShotChord chord)
    {
        if (recording is not { } action)
            return;
        var next = keys.Bind(action, chord.ToString());
        try
        {
            new CaptureOptions { Shortcuts = next }.Validate();
            keys = next;
            feedback.Text = "点击「应用设置」保存按键。";
            StopRecording();
        }
        catch (FormatException ex)
        {
            feedback.Text = ex.Message;
        }
    }

    private void CaptureKey(object sender, KeyEventArgs e)
    {
        if (recording is null)
            return;
        if (e.Key == Key.Escape)
        {
            feedback.Text = "";
            StopRecording();
            e.Handled = true;
            return;
        }

        if (ShotChord.FromEvent(e, out var chord, CaptureOptions.AllowsSingleKey(recording.Value)))
            Accept(chord);
        e.Handled = true;
    }
}
