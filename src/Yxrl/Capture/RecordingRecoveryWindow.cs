using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Yxrl.Desktop;
using Yxrl.Views;

namespace Yxrl.Capture;

internal sealed class RecordingRecoveryWindow : Window
{
    private sealed record Entry(RecordingSession Session)
    {
        public override string ToString() =>
            $"{Session.Header.Started.ToLocalTime():yyyy-MM-dd HH:mm:ss}   ·   {Session.Header.Area.Width} × {Session.Header.Area.Height} px";
    }

    private readonly ListBox items = new()
    {
        MinHeight = 84,
        MaxHeight = 220,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent
    };
    private readonly Button restore = new() { Content = "恢复所选录屏" }, discard = new() { Content = "丢弃所选录屏" },
        later = new() { Content = "稍后处理" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
    private bool busy, closed, micaEnabled;

    public RecordingRecoveryWindow(IEnumerable<RecordingSession> sessions, Action<RecordedClip> open)
    {
        Title = "YXRL 恢复录屏";
        if (Theme.AppIcon is { } appIcon) Icon = appIcon;
        AutomationProperties.SetAutomationId(this, "YxrlRecordingRecovery");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Width = 460;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Ink");

        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        Content = panel;

        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var badge = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 11, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "\uE722",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        badge.SetResourceReference(Border.BackgroundProperty, "Hot");
        ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        DockPanel.SetDock(badge, Dock.Left);
        headerDock.Children.Add(badge);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = "发现上次未导出的录屏", FontSize = 15, FontWeight = FontWeights.SemiBold });
        var sub = new TextBlock { Text = "可打开已完成片段继续剪辑、调音或导出", FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        titleStack.Children.Add(sub);
        headerDock.Children.Add(titleStack);
        panel.Children.Add(headerDock);

        AutomationProperties.SetName(items, "可恢复的录屏");
        foreach (var session in sessions) items.Items.Add(new Entry(session));
        items.SelectedIndex = 0;
        var listCard = new Border { Child = items, Margin = new Thickness(0, 0, 0, 12) };
        listCard.SetResourceReference(StyleProperty, "GroupCard");
        listCard.Padding = new Thickness(6);
        panel.Children.Add(listCard);

        var buttons = new DockPanel { LastChildFill = false };
        restore.SetResourceReference(StyleProperty, "PrimaryButton");
        discard.SetResourceReference(StyleProperty, "SecondaryButton");
        discard.SetResourceReference(ForegroundProperty, "Danger");
        later.SetResourceReference(StyleProperty, "SecondaryButton");
        discard.MinHeight = later.MinHeight = restore.MinHeight = 30;
        DockPanel.SetDock(later, Dock.Left);
        buttons.Children.Add(later);
        DockPanel.SetDock(restore, Dock.Right);
        buttons.Children.Add(restore);
        discard.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(discard, Dock.Right);
        buttons.Children.Add(discard);
        panel.Children.Add(buttons);

        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(status);
        SourceInitialized += (_, _) => ApplyChrome();
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

    internal void RefreshTheme() => ApplyChrome();

    private void ApplyChrome()
    {
        micaEnabled = Theme.ApplyWindowChrome(this, Native.BackdropKind.Mica);
        if (micaEnabled) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "Surface");
    }

    private void UpdateButtons()
    {
        restore.IsEnabled = discard.IsEnabled = !busy && items.SelectedItem is not null;
        items.IsEnabled = !busy;
    }
}
