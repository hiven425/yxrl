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

internal sealed class ValuePinWindow : Window
{
    public ValuePinWindow(string value, PixelRect? placement)
    {
        bool isColor = ShotColor.TryParse(value, out var color);
        string label = isColor ? "颜色贴图" : "文字贴图";
        Title = "YXRL " + label;
        AutomationProperties.SetName(this, Title);
        Topmost = true;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = isColor ? 264 : 340;
        SizeToContent = SizeToContent.Height;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Surface");
        if (Theme.AppIcon is { } icon) Icon = icon;

        var root = new StackPanel { Margin = new Thickness(10) };
        Content = root;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Background = Brushes.Transparent };
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var close = Button("关闭", Close);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(caption);
        header.MouseLeftButtonDown += (_, _) => { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
        root.Children.Add(header);
        var status = new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        void Copy(string text)
        {
            try { Clipboard.SetText(text); status.Text = "已复制"; }
            catch (ExternalException) { status.Text = "剪贴板正忙，请重试。"; }
        }

        if (isColor)
        {
            var swatch = new Border { Height = 57, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(color.MediaColor) };
            AutomationProperties.SetName(swatch, color.Format(0));
            root.Children.Add(swatch);
            for (int format = 0; format < 4; format++)
            {
                string text = color.Format(format);
                var copy = Button(text, () => Copy(text));
                copy.HorizontalContentAlignment = HorizontalAlignment.Left;
                copy.Margin = new Thickness(0, 5, 0, 0);
                AutomationProperties.SetName(copy, "复制 " + text);
                root.Children.Add(copy);
            }
        }
        else
        {
            var editor = new TextBox
            {
                IsReadOnly = true, Text = value, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300, Padding = new Thickness(8)
            };
            AutomationProperties.SetName(editor, "贴图文字");
            root.Children.Add(editor);
            var copy = Button("复制文字", () => Copy(editor.SelectionLength > 0 ? editor.SelectedText : value));
            copy.Margin = new Thickness(0, 5, 0, 0);
            root.Children.Add(copy);
        }
        root.Children.Add(status);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        if (placement is { } area)
        {
            Loaded += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var work = DesktopFrame.WorkAreaAt(area.Center);
                if (!work.HasArea) return;
                Native.SetWindowPos(hwnd, Native.Topmost,
                    Math.Clamp(area.Left, work.Left, Math.Max(work.Left, work.Right - area.Width)),
                    Math.Clamp(area.Top, work.Top, Math.Max(work.Top, work.Bottom - area.Height)), 0, 0,
                    Native.NoSize | Native.NoActivate);
            };
        }
    }

    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4) };
        button.SetResourceReference(StyleProperty, "SecondaryButton");
        button.Click += (_, _) => action();
        return button;
    }
}
