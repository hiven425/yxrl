using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yxrl.Capture;

internal sealed class RecordingRecoveryWindow : Window
{
    private sealed record Entry(RecordingSession Session)
    {
        public override string ToString() =>
            $"{Session.Header.Started.ToLocalTime():yyyy-MM-dd HH:mm:ss}   {Session.Header.Area.Width} × {Session.Header.Area.Height} px";
    }

    private readonly ListBox items = new() { MinHeight = 82, MaxHeight = 220 };
    private readonly Button restore = new() { Content = "恢复所选录屏" }, discard = new() { Content = "丢弃所选录屏" },
        later = new() { Content = "稍后处理" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private bool busy, closed;

    public RecordingRecoveryWindow(IEnumerable<RecordingSession> sessions, Action<RecordedClip> open)
    {
        Title = "YXRL 恢复录屏";
        if (Views.Theme.AppIcon is { } appIcon) Icon = appIcon;
        AutomationProperties.SetAutomationId(this, "YxrlRecordingRecovery");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Width = 440;
        SetResourceReference(BackgroundProperty, "Surface");
        var panel = new StackPanel { Margin = new Thickness(15) };
        Content = panel;
        panel.Children.Add(new TextBlock
        {
            Text = "发现上次留下的录屏。可打开已完成的片段继续剪辑或导出。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 9)
        });
        AutomationProperties.SetName(items, "可恢复的录屏");
        foreach (var session in sessions) items.Items.Add(new Entry(session));
        items.SelectedIndex = 0;
        panel.Children.Add(items);
        var buttons = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        restore.SetResourceReference(StyleProperty, "PrimaryButton");
        foreach (var button in new[] { restore, discard, later })
        {
            button.MinHeight = 29;
            button.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        panel.Children.Add(status);
        items.SelectionChanged += (_, _) => UpdateButtons();
        later.Click += (_, _) => Close();
        discard.Click += (_, _) =>
        {
            if (items.SelectedItem is not Entry entry ||
                MessageBox.Show(this, "永久丢弃所选录屏的恢复文件？", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            entry.Session.Delete();
            items.Items.Remove(entry);
            items.SelectedIndex = 0;
            if (items.Items.Count == 0) Close();
        };
        restore.Click += async (_, _) =>
        {
            if (items.SelectedItem is not Entry entry || busy)
                return;
            busy = true;
            status.Text = "正在打开已完成的片段…";
            UpdateButtons();
            try
            {
                var clip = await Task.Run(() => RecordingAssembler.Prepare(entry.Session));
                if (closed) return;
                items.Items.Remove(entry);
                items.SelectedIndex = 0;
                open(clip);
                if (items.Items.Count == 0) Close();
                else status.Text = "已打开所选录屏。";
            }
            catch (Exception ex) when (ex is IOException or ExternalException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (!closed) status.Text = "恢复失败，片段仍保存在本机。 " + ex.Message;
            }
            finally
            {
                busy = false;
                if (!closed) UpdateButtons();
            }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => closed = true;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        restore.IsEnabled = discard.IsEnabled = !busy && items.SelectedItem is not null;
        items.IsEnabled = !busy;
    }
}
