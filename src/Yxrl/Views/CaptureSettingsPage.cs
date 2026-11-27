using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yxrl.Capture;

namespace Yxrl.Views;

internal sealed class CaptureSettingsPage : StackPanel
{
    public const int Section = 3;
    private readonly CheckBox enabled = new() { Content = "启用截图工具" };
    private readonly TextBox address = new(), model = new();
    private readonly ComboBox target = new() { IsEditable = true };
    private readonly PasswordBox password = new();
    private readonly CheckBox clearSecret = new() { Content = "删除此地址已保存的密钥", Margin = new Thickness(0, 8, 0, 2) };
    private readonly Button testButton = new() { Content = "测试连接", Padding = new Thickness(12, 4, 12, 4), MinHeight = 30 };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock testStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border testStatusPill = new() { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 5, 10, 5), Visibility = Visibility.Collapsed, Margin = new Thickness(10, 0, 0, 0) };
    private readonly StackPanel keyList = new();
    private readonly Dictionary<ShotAction, Button> recorders = new();
    private readonly Dictionary<ShotAction, Button> clearButtons = new();
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
        // Card 1: Master switch & bundled offline OCR badge
        var toggleCard = new StackPanel();
        var masterRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        enabled.SetResourceReference(StyleProperty, "Switch");
        AutomationProperties.SetName(enabled, "启用截图工具");
        enabled.Checked += (_, _) => UpdateEnabledVisuals();
        enabled.Unchecked += (_, _) => UpdateEnabledVisuals();
        DockPanel.SetDock(enabled, Dock.Right);
        masterRow.Children.Add(enabled);

        var masterText = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        masterText.Children.Add(new TextBlock { Text = "启用截图工具", FontSize = 14, FontWeight = FontWeights.SemiBold });
        var toggleHint = Muted("控制全局热键（Ctrl+1 截图、Ctrl+2 贴图、Ctrl+3 恢复贴图）与月历底栏截图入口");
        toggleHint.Margin = new Thickness(0, 2, 0, 0);
        masterText.Children.Add(toggleHint);
        masterRow.Children.Add(masterText);
        toggleCard.Children.Add(masterRow);

        var ocrBanner = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 10, 0, 0),
            BorderThickness = new Thickness(1)
        };
        ocrBanner.SetResourceReference(Border.BackgroundProperty, "Card");
        ocrBanner.SetResourceReference(Border.BorderBrushProperty, "Line");
        var ocrInfo = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var ocrGlyph = new TextBlock
        {
            Text = "\uE8EF",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ocrGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        ocrInfo.Children.Add(ocrGlyph);
        var ocrText = new TextBlock
        {
            Text = "本地文字提取 · 已内置 PP-OCRv5 离线模型（识别全程在本机完成，无需联网）",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        ocrText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        ocrInfo.Children.Add(ocrText);
        ocrBanner.Child = ocrInfo;
        toggleCard.Children.Add(ocrBanner);
        Children.Add(Card(toggleCard, topMargin: 0));

        // Card 2: Shortcuts
        var keyStack = new StackPanel();
        var keyHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var restore = new Button
        {
            Content = "恢复默认快捷键",
            Padding = new Thickness(10, 3, 10, 3),
            MinHeight = 27,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        restore.SetResourceReference(StyleProperty, "SecondaryButton");
        restore.Click += (_, _) =>
        {
            StopRecording();
            keys = new();
            Refresh();
        };
        DockPanel.SetDock(restore, Dock.Right);
        keyHeader.Children.Add(restore);

        var keyTitleGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        keyTitleGroup.Children.Add(SectionTitle("快捷键", margin: new Thickness(0)));
        var keyTip = Muted("点击键帽录入，Esc 取消");
        keyTip.Margin = new Thickness(10, 0, 0, 0);
        keyTip.VerticalAlignment = VerticalAlignment.Center;
        keyTitleGroup.Children.Add(keyTip);
        keyHeader.Children.Add(keyTitleGroup);
        keyStack.Children.Add(keyHeader);

        var actions = Enum.GetValues<ShotAction>();
        for (int idx = 0; idx < actions.Length; idx++)
        {
            var action = actions[idx];
            bool last = idx == actions.Length - 1;
            var itemStack = new StackPanel();
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6), VerticalAlignment = VerticalAlignment.Center };

            var clear = new Button
            {
                Content = "停用",
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 3, 10, 3),
                MinHeight = 28,
                FontSize = 12
            };
            clear.SetResourceReference(StyleProperty, "SecondaryButton");
            AutomationProperties.SetName(clear, "停用" + CaptureOptions.Label(action));
            clear.Click += (_, _) =>
            {
                StopRecording();
                keys = keys.Bind(action, "");
                Refresh();
            };
            clearButtons.Add(action, clear);
            DockPanel.SetDock(clear, Dock.Right);
            row.Children.Add(clear);

            var button = new Button
            {
                MinWidth = 102,
                MinHeight = 28
            };
            button.SetResourceReference(StyleProperty, "KeycapButton");
            AutomationProperties.SetName(button, CaptureOptions.Label(action) + "快捷键");
            recorders.Add(action, button);
            button.Click += (_, _) => StartRecording(action);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);

            var labelPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            labelPanel.Children.Add(new TextBlock { Text = CaptureOptions.Label(action), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            bool isGlobal = action is ShotAction.Start or ShotAction.Clipboard or ShotAction.RestorePin;
            labelPanel.Children.Add(Badge(isGlobal ? "全局" : "工具内", isGlobal));
            row.Children.Add(labelPanel);
            itemStack.Children.Add(row);

            var error = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = Visibility.Collapsed
            };
            error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            warnings[action] = error;
            itemStack.Children.Add(error);

            var rowBorder = new Border
            {
                Child = itemStack,
                BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1)
            };
            rowBorder.SetResourceReference(Border.BorderBrushProperty, "Line");
            keyList.Children.Add(rowBorder);
        }
        keyStack.Children.Add(keyList);
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        keyStack.Children.Add(feedback);
        Children.Add(Card(keyStack));

        // Card 3: Translation API (compact two-column form)
        var trStack = new StackPanel();
        var trHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var presetsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        presetsPanel.Children.Add(PresetChip("Ollama 本机", "http://localhost:11434/v1", "qwen2.5:7b"));
        presetsPanel.Children.Add(PresetChip("OpenAI", "https://api.openai.com/v1", "gpt-4o-mini"));
        DockPanel.SetDock(presetsPanel, Dock.Right);
        trHeader.Children.Add(presetsPanel);
        trHeader.Children.Add(SectionTitle("翻译服务", margin: new Thickness(0)));
        trStack.Children.Add(trHeader);
        trStack.Children.Add(Muted("兼容 OpenAI Chat Completions 接口，密钥保存在 Windows 凭据管理器，仅主动点击翻译时发送选定文本。"));

        address.ToolTip = "例如 http://localhost:11434/v1";
        Field(trStack, "API 基础地址", address);

        var twoCol = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });

        var modelCol = new StackPanel();
        model.ToolTip = "例如 qwen2.5:7b 或 gpt-4o-mini";
        Field(modelCol, "模型名", model, topMargin: 0);
        Grid.SetColumn(modelCol, 0);
        twoCol.Children.Add(modelCol);

        foreach (var preset in new[] { "简体中文", "繁體中文", "英文", "日文", "韩文", "法文", "德文" })
            target.Items.Add(preset);
        var targetCol = new StackPanel();
        Field(targetCol, "默认目标语言", target, topMargin: 0);
        Grid.SetColumn(targetCol, 2);
        twoCol.Children.Add(targetCol);
        trStack.Children.Add(twoCol);

        Field(trStack, "API 密钥（可选，留空保留原密钥）", password);
        trStack.Children.Add(clearSecret);

        testButton.SetResourceReference(StyleProperty, "SecondaryButton");
        testButton.Click += (_, _) => _ = TestTranslationAsync();
        testStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        testStatusPill.Child = testStatus;

        var testRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(testButton, Dock.Left);
        testRow.Children.Add(testButton);
        testRow.Children.Add(testStatusPill);
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

    private Button PresetChip(string title, string url, string defaultModel)
    {
        var chip = new Button
        {
            Content = title,
            FontSize = 11.5,
            Padding = new Thickness(8, 2, 8, 2),
            MinHeight = 24,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = $"填入 {url}"
        };
        chip.SetResourceReference(StyleProperty, "SecondaryButton");
        chip.Click += (_, _) =>
        {
            address.Text = url;
            if (string.IsNullOrWhiteSpace(model.Text))
                model.Text = defaultModel;
        };
        return chip;
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

    private static TextBlock SectionTitle(string title, Thickness? margin = null) => new()
    {
        Text = title,
        FontWeight = FontWeights.SemiBold,
        FontSize = 14.5,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = margin ?? new Thickness(0, 0, 0, 6)
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
            Padding = new Thickness(6, 1.5, 6, 1.5),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = label
        };
        badge.SetResourceReference(Border.BackgroundProperty, accent ? "Hot" : "Card");
        return badge;
    }

    private static void Field(StackPanel parent, string label, Control control, double topMargin = 8)
    {
        var title = new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, topMargin, 0, 4) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        parent.Children.Add(title);
        AutomationProperties.SetName(control, label);
        parent.Children.Add(control);
    }

    private void UpdateEnabledVisuals()
    {
        keyList.Opacity = enabled.IsChecked == true ? 1.0 : 0.65;
    }

    public void Load(Preferences settings, CaptureTools runtime)
    {
        StopRecording();
        testCancel?.Cancel();
        testStatus.Text = "";
        testStatusPill.Visibility = Visibility.Collapsed;
        tools = runtime;
        enabled.IsChecked = settings.Capture.Enabled;
        keys = settings.Capture.Shortcuts;
        address.Text = settings.Translation.BaseUrl;
        model.Text = settings.Translation.Model;
        target.Text = settings.Translation.TargetLanguage;
        password.Clear();
        clearSecret.IsChecked = false;
        SetFeedback("");
        foreach (var warning in warnings.Values)
            warning.Visibility = Visibility.Collapsed;
        foreach (var error in tools.Hotkeys.Errors)
            ShowConflict(error.Key, error.Value);
        UpdateEnabledVisuals();
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
        SetTestFeedback("正在发送测试请求…", "Card", "Muted");
        try
        {
            var opts = Translation;
            var endpoint = opts.Endpoint() ?? throw new FormatException("请先填写 API 基础地址和模型名。");
            var key = EditedSecret is { Length: > 0 } ? EditedSecret
                : EditedSecret == "" ? null : TranslationCredential.Read(endpoint);
            var reply = await tools.Translator.Translate(opts, "Hello", string.IsNullOrWhiteSpace(opts.TargetLanguage) ? "简体中文" : opts.TargetLanguage, key, token);
            if (!token.IsCancellationRequested)
                SetTestFeedback($"连接成功：{reply}", "WorkFill", "Positive");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or OperationCanceledException or IOException or JsonException or Win32Exception or InvalidDataException)
        {
            if (!token.IsCancellationRequested)
                SetTestFeedback(ex is OperationCanceledException ? "请求超时，请检查服务是否运行。" : ex.Message, "RestFill", "Danger");
        }
        finally
        {
            testButton.IsEnabled = true;
        }
    }

    private void SetTestFeedback(string text, string backgroundKey, string foregroundKey)
    {
        testStatus.Text = text;
        testStatus.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        testStatusPill.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        testStatusPill.Visibility = Visibility.Visible;
    }

    private void SetFeedback(string text)
    {
        feedback.Text = text;
        feedback.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowConflict(ShotAction action, string message)
    {
        warnings[action].Text = message;
        warnings[action].Visibility = Visibility.Visible;
    }

    private void Refresh()
    {
        foreach (var (action, b) in recorders)
        {
            bool isRecording = recording == action;
            bool empty = keys[action].Length == 0;
            b.Content = isRecording ? (CaptureOptions.AllowsSingleKey(action) ? "请按快捷键" : "请按组合键")
                : empty ? "已停用" : keys[action];
            b.SetResourceReference(Control.BackgroundProperty, isRecording ? "Hot" : "Card");
            b.SetResourceReference(Control.BorderBrushProperty, isRecording ? "Accent" : "PanelBorder");
            b.SetResourceReference(Control.ForegroundProperty, isRecording ? "Accent" : empty ? "Faint" : "Ink");
            clearButtons[action].IsEnabled = !empty;
        }
    }

    private void StartRecording(ShotAction action)
    {
        StopRecording();
        recording = action;
        warnings[action].Visibility = Visibility.Collapsed;
        SetFeedback(CaptureOptions.AllowsSingleKey(action) ? "按单键或组合键录入，Esc 取消。" : "按组合键录入，Esc 取消。");
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
            SetFeedback("点击「应用设置」保存按键。");
            StopRecording();
        }
        catch (FormatException ex)
        {
            SetFeedback(ex.Message);
        }
    }

    private void CaptureKey(object sender, KeyEventArgs e)
    {
        if (recording is null)
            return;
        if (e.Key == Key.Escape)
        {
            SetFeedback("");
            StopRecording();
            e.Handled = true;
            return;
        }

        if (ShotChord.FromEvent(e, out var chord, CaptureOptions.AllowsSingleKey(recording.Value)))
            Accept(chord);
        e.Handled = true;
    }
}
