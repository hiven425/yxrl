using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Yxrl.Capture;

namespace Yxrl.Views;

internal sealed class CaptureSettingsPage : StackPanel
{
    public const int Section = 3;
    private readonly CheckBox enabled = new()
    {
        Content = "启用截图工具"
    };
    private readonly TextBox address = new(), model = new(), target = new();
    private readonly PasswordBox password = new();
    private readonly CheckBox clearSecret = new()
    {
        Content = "删除此地址已保存的密钥"
    };
    private readonly TextBlock feedback = new()
    {
        TextWrapping = TextWrapping.Wrap
    };
    private readonly Dictionary<ShotAction, Button> recorders = new();
    private readonly Dictionary<ShotAction, TextBlock> warnings = new();
    private CaptureTools? tools;
    private ShotShortcuts keys = new();
    private ShotAction? recording;
    public CaptureOptions Options => new()
    {
        Enabled = enabled.IsChecked == true,
        Shortcuts = keys
    };
    public TranslationOptions Translation => new(address.Text.Trim(), model.Text.Trim(), target.Text.Trim());
    public string? EditedSecret => clearSecret.IsChecked == true ? "" : password.Password.Length == 0 ? null : password.Password;

    public CaptureSettingsPage()
    {
        Children.Add(enabled);
        Header("快捷键");
        foreach (var action in Enum.GetValues<ShotAction>())
        {
            var row = new DockPanel
            {
                Margin = new Thickness(0, 2, 0, 2)
            };
            var clear = new Button
            {
                Content = "停用",
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(8, 3, 8, 3)
            };
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
                MinWidth = 100,
                Padding = new Thickness(8, 3, 8, 3)
            };
            AutomationProperties.SetName(button, CaptureOptions.Label(action) + "快捷键");
            recorders.Add(action, button);
            button.Click += (_, _) => StartRecording(action);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            row.Children.Add(new TextBlock { Text = CaptureOptions.Label(action) + (action is ShotAction.Start or ShotAction.Clipboard ? "（全局）" : "（工具内）"), VerticalAlignment = VerticalAlignment.Center });
            Children.Add(row);
            var error = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            warnings[action] = error;
            Children.Add(error);
        }

        var restore = new Button
        {
            Content = "恢复默认快捷键",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        restore.Click += (_, _) =>
        {
            StopRecording();
            keys = new();
            Refresh();
        };
        Children.Add(restore);
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        Children.Add(feedback);
        Header("翻译服务");
        Field("API 基础地址", address);
        address.ToolTip = "例如 http://localhost:11434/v1";
        Field("模型名", model);
        Field("默认目标语言", target);
        Field("API 密钥（可选，留空保留原密钥）", password);
        Children.Add(clearSecret);
        PreviewKeyDown += CaptureKey;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                StopRecording();
        };
    }

    private void Header(string title) => Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 16, Margin = new Thickness(0, 12, 0, 6) });
    private void Field(string label, Control control)
    {
        Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
        AutomationProperties.SetName(control, label);
        Children.Add(control);
    }

    public void Load(Preferences settings, CaptureTools runtime)
    {
        StopRecording();
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

    public void ShowConflict(ShotAction action, string message)
    {
        warnings[action].Text = message;
        warnings[action].Visibility = Visibility.Visible;
    }

    private void Refresh()
    {
        foreach (var (action, b) in recorders)
            b.Content = recording == action ? CaptureOptions.AllowsSingleKey(action) ? "请按快捷键" : "请按组合键"
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
            new CaptureOptions
            {
                Shortcuts = next
            }.Validate();
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
