using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal enum ImageAction
{
    Copy,
    Save,
    Pin,
    Ocr,
    Translate
}

internal sealed class ShotWindow : Window
{
    private readonly DesktopFrame? desktop;
    private readonly CaptureTools tools;
    private readonly ShotSurface surface;
    private readonly Canvas chrome = new()
    {
        ClipToBounds = true
    };
    private readonly TextBlock dimensions = new()
    {
        Margin = new Thickness(7, 3, 7, 3)
    };
    private readonly Border info, toolbar;
    private readonly WrapPanel buttons = new();
    private readonly Dictionary<MarkKind, Button> marks = new();
    private readonly Button undo, redo;
    private bool editing;
    public bool Selecting => desktop is not null;

    public ShotWindow(CaptureTools tools, ShotDocument document, DesktopFrame? desktop = null, PixelRect? placement = null)
    {
        this.tools = tools;
        this.desktop = desktop;
        editing = Selecting;
        Title = Selecting ? "YXRL 截图选区" : "YXRL 桌面贴图";
        AutomationProperties.SetAutomationId(this, Selecting ? "YxrlSelection" : "YxrlImagePin");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        Background = Brushes.Black;
        surface = new ShotSurface(document, desktop)
        {
            EnterText = _ => AskText()
        };
        AutomationProperties.SetName(surface, Selecting ? "截图画布" : "贴图画布");
        var root = new Grid
        {
            Background = Brushes.Transparent
        };
        Content = root;
        root.Children.Add(surface);
        root.Children.Add(chrome);
        info = Panel(dimensions);
        toolbar = Panel(buttons);
        chrome.Children.Add(info);
        chrome.Children.Add(toolbar);
        foreach (var (tool, label, sign) in new[]
        {
            (MarkKind.Rectangle, "矩形", "□"),
            (MarkKind.Arrow, "箭头", "↗"),
            (MarkKind.Pen, "画笔", "✎"),
            (MarkKind.Text, "文字", "T"),
            (MarkKind.Mosaic, "马赛克", "▦")
        })
            marks[tool] = Add(sign, label, () =>
            {
                surface.Tool = surface.Tool == tool ? null : tool;
                surface.Focus();
                UpdateChrome();
            });
        undo = Add("↶", "撤销", () =>
        {
            surface.Document.Undo();
            surface.Refresh();
        });
        redo = Add("↷", "重做", () =>
        {
            surface.Document.Redo();
            surface.Refresh();
        });
        Add("提取文字", "提取文字", () => Run(ImageAction.Ocr));
        Add("翻译", "打开翻译", () => Run(ImageAction.Translate));
        if (Selecting)
            Add("钉住", "钉住选区", () => Run(ImageAction.Pin));
        Add("保存", "保存 PNG", () => Run(ImageAction.Save));
        Add("复制", "复制图片", () => Run(ImageAction.Copy));
        Add(Selecting ? "×" : "完成", Selecting ? "取消截图" : "完成标注", () =>
        {
            if (Selecting)
                Close();
            else
                ToggleEditing();
        });
        if (!Selecting)
        {
            ContextMenu = new ContextMenu();
            void Item(string title, Action action)
            {
                var item = new MenuItem
                {
                    Header = title
                };
                item.Click += (_, _) => action();
                ContextMenu.Items.Add(item);
            }

            Item("标注", ToggleEditing);
            Item("提取文字", () => Run(ImageAction.Ocr));
            Item("翻译", () => Run(ImageAction.Translate));
            Item("复制图片", () => Run(ImageAction.Copy));
            Item("保存 PNG", () => Run(ImageAction.Save));
            Item("关闭贴图", Close);
        }

        var work = SystemParameters.WorkArea;
        double fit = Math.Min(1, Math.Min(work.Width * .85 / document.Width, work.Height * .85 / document.Height));
        Width = Math.Max(1, document.Width * fit);
        Height = Math.Max(1, document.Height * fit);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
        var initial = desktop?.Bounds ?? placement;
        if (initial is { } screen)
        {
            void Place() => Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.Topmost, screen.Left, screen.Top, screen.Width, screen.Height, Native.NoOwnerZOrder);
            SourceInitialized += (_, _) => Place();
            Loaded += (_, _) => Place();
        }

        surface.Changed += UpdateChrome;
        SizeChanged += (_, _) => UpdateChrome();
        toolbar.SizeChanged += (_, _) => PositionBars();
        info.SizeChanged += (_, _) => PositionBars();
        Loaded += (_, _) =>
        {
            UpdateChrome();
            Activate();
            surface.Focus();
        };
        PreviewKeyDown += OnKey;
        root.MouseLeftButtonDown += (_, e) =>
        {
            if (Selecting || editing || e.ChangedButton != MouseButton.Left)
                return;
            if (e.ClickCount == 2)
                Close();
            else
                DragMove();
        };
        root.MouseWheel += (_, e) =>
        {
            if (Selecting || editing)
                return;
            double zoom = Math.Clamp(Width / document.Width * (e.Delta > 0 ? 1.1 : 1 / 1.1), .05, 8);
            Width = Math.Max(1, document.Width * zoom);
            Height = Math.Max(1, document.Height * zoom);
            e.Handled = true;
        };
        UpdateChrome();
    }

    private static Border Panel(UIElement child)
    {
        var border = new Border
        {
            Child = child,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "Paper");
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        return border;
    }

    private Button Add(string text, string name, Action action)
    {
        var b = new Button
        {
            Content = text,
            ToolTip = name,
            Padding = new Thickness(6, 3, 6, 3),
            MinWidth = 28,
            MinHeight = 28
        };
        AutomationProperties.SetName(b, name);
        b.ToolTipOpening += (_, _) =>
        {
            var keys = tools.Options.Shortcuts;
            string? shortcut = name switch
            {
                "提取文字" => keys.Ocr,
                "打开翻译" => keys.Translate,
                "钉住选区" => keys.Pin,
                "保存 PNG" => "Ctrl+S",
                "复制图片" => "Ctrl+C",
                "撤销" => "Ctrl+Z",
                "重做" => "Ctrl+Y",
                "取消截图" => "Esc",
                _ => null
            };
            b.ToolTip = string.IsNullOrEmpty(shortcut) ? name : $"{name}（{shortcut}）";
        };
        b.Click += (_, _) => action();
        buttons.Children.Add(b);
        return b;
    }

    private string? AskText()
    {
        var input = new TextBox
        {
            MinWidth = 220,
            Margin = new Thickness(0, 0, 0, 10),
            AcceptsReturn = false
        };
        var body = new StackPanel
        {
            Margin = new Thickness(14)
        };
        body.Children.Add(input);
        var ok = new Button
        {
            Content = "添加文字",
            IsDefault = true
        };
        body.Children.Add(ok);
        var dialog = new Window
        {
            Owner = this,
            Title = "标注文字",
            Content = body,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true
        };
        dialog.SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetName(input, "标注文字");
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                dialog.DialogResult = false;
        };
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void ToggleEditing()
    {
        editing = !editing;
        surface.Tool = null;
        UpdateChrome();
        surface.Focus();
    }

    private void UpdateChrome()
    {
        surface.IsHitTestVisible = Selecting || editing;
        bool show = editing && surface.Selection.HasArea;
        toolbar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        info.Visibility = Selecting && show ? Visibility.Visible : Visibility.Collapsed;
        buttons.MaxWidth = Math.Max(1, ToolArea().Width - 8);
        undo.IsEnabled = surface.Document.CanUndo;
        redo.IsEnabled = surface.Document.CanRedo;
        foreach (var (kind, b) in marks)
            b.SetResourceReference(Control.BackgroundProperty, surface.Tool == kind ? "Hot" : "Paper");
        if (desktop is not null)
        {
            var r = surface.Selection.Offset(desktop.Bounds.Left, desktop.Bounds.Top);
            dimensions.Text = $"{r.Left}, {r.Top}   ·   {r.Width} × {r.Height} px";
        }

        PositionBars();
    }

    private Rect ToolArea() => desktop is null ? new Rect(RenderSize) : surface.ViewRect(desktop.ToolsArea(surface.Selection));
    private void PositionBars()
    {
        if (toolbar.Visibility != Visibility.Visible || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var r = surface.ViewRect(surface.Selection);
        double width = toolbar.ActualWidth, height = toolbar.ActualHeight;
        var area = ToolArea();
        double Axis(double value, double start, double end, double length) => Math.Clamp(value, start, Math.Max(start, end - length));
        double x = Axis(r.Left, area.Left, area.Right, width);
        double y = r.Bottom + 6 + height <= area.Bottom ? r.Bottom + 6 : r.Top - height - 6;
        if (y < area.Top)
            y = r.Bottom - height - 3;
        y = Axis(y, area.Top, area.Bottom, height);
        Canvas.SetLeft(toolbar, x);
        Canvas.SetTop(toolbar, y);
        double infoX = Axis(r.Left, area.Left, area.Right, info.ActualWidth);
        double infoY = r.Top - info.ActualHeight - 4;
        if (infoY < area.Top || new Rect(x, y, width, height).IntersectsWith(new Rect(infoX, infoY, info.ActualWidth, info.ActualHeight)))
            infoY = r.Top + 4;
        Canvas.SetLeft(info, infoX);
        Canvas.SetTop(info, Axis(infoY, area.Top, area.Bottom, info.ActualHeight));
    }

    internal void Run(ImageAction action)
    {
        if (!surface.Selection.HasArea)
            return;
        var rect = surface.Selection;
        var image = Selecting ? surface.Document.Crop(rect) : surface.Document;
        PixelRect? place = desktop is null ? null : rect.Offset(desktop.Bounds.Left, desktop.Bounds.Top);
        if (tools.Output(this, action, image, place) && Selecting)
            Close();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (!Selecting && e.Key == Key.Space && mods == ModifierKeys.None)
        {
            ToggleEditing();
            e.Handled = true;
            return;
        }

        if (Selecting && !surface.Selection.HasArea && e.Key == Key.Enter && mods == ModifierKeys.None)
        {
            surface.SelectWindowAtPointer();
            e.Handled = true;
            return;
        }

        if (ShotChord.FromEvent(e, out var chord))
        {
            var shortcuts = tools.Options.Shortcuts;
            foreach (var action in new[]
            {
                ShotAction.Pin,
                ShotAction.Ocr,
                ShotAction.Translate
            })
            {
                if (action == ShotAction.Pin && !Selecting)
                    continue;
                if (!ShotChord.TryParse(shortcuts[action], out var configured) || configured != chord)
                    continue;
                Run(action == ShotAction.Pin ? ImageAction.Pin : action == ShotAction.Ocr ? ImageAction.Ocr : ImageAction.Translate);
                e.Handled = true;
                return;
            }
        }

        if (mods == ModifierKeys.Control && e.Key is Key.Z or Key.Y && editing)
        {
            if (e.Key == Key.Z)
                surface.Document.Undo();
            else
                surface.Document.Redo();
            surface.Refresh();
        }
        else if ((e.Key == Key.Enter && mods == ModifierKeys.None && Keyboard.FocusedElement is not Button) || (mods == ModifierKeys.Control && e.Key == Key.C))
            Run(ImageAction.Copy);
        else if (mods == ModifierKeys.Control && e.Key == Key.S)
            Run(ImageAction.Save);
        else if (Selecting && mods == ModifierKeys.None && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            surface.Nudge(e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0);
        else
            return;
        e.Handled = true;
    }
}
