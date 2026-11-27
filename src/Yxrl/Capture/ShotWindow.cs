using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Globalization;
using System.Runtime.InteropServices;
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
    private readonly bool recordingSelection;
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
    private readonly WrapPanel buttons = new(), styleControls = new();
    private readonly ComboBox color = new(), size = new(), font = new();
    private readonly TextBox number = new() { Width = 51, MaxLength = 4 };
    private readonly CheckBox bold = new() { Content = "粗体" }, back = new() { Content = "背景" }, square = new() { Content = "方形" };
    private readonly CheckBox italic = new() { Content = "斜体" }, outline = new() { Content = "描边" };
    private readonly Button numberNote = new() { Content = "编号说明" };
    private readonly TextBlock numberLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<MarkKind, Button> marks = new();
    private readonly Button undo, redo;
    private bool editing, updatingStyles;
    private Size? pinSize;
    public bool Selecting => desktop is not null;

    public ShotWindow(CaptureTools tools, ShotDocument document, DesktopFrame? desktop = null, PixelRect? placement = null, bool recordingSelection = false)
    {
        this.tools = tools;
        this.desktop = desktop;
        this.recordingSelection = recordingSelection;
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
            EnterText = AskText
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
        var toolsPanel = new StackPanel();
        toolsPanel.Children.Add(buttons);
        toolsPanel.Children.Add(styleControls);
        toolbar = Panel(toolsPanel);
        chrome.Children.Add(info);
        chrome.Children.Add(toolbar);
        foreach (var (tool, label, sign) in new[]
        {
            (MarkKind.Rectangle, "矩形", "□"),
            (MarkKind.Ellipse, "椭圆", "○"),
            (MarkKind.Arrow, "箭头", "↗"),
            (MarkKind.Line, "线段", "／"),
            (MarkKind.Pen, "画笔", "✎"),
            (MarkKind.Highlight, "荧光笔", "荧光"),
            (MarkKind.Text, "添加文字", "文字"),
            (MarkKind.Number, "序号", "序号"),
            (MarkKind.Mosaic, "马赛克", "▦")
        })
            marks[tool] = Add(sign, label, () =>
            {
                surface.UseTool(surface.Tool == tool ? null : tool);
                surface.Focus();
            });
        BuildStyleControls();
        undo = Add("↶", "撤销", () =>
        {
            surface.History(false);
        });
        redo = Add("↷", "重做", () =>
        {
            surface.History(true);
        });
        Add("提取文字", "提取文字", () => Run(ImageAction.Ocr));
        Add("翻译", "打开翻译", () => Run(ImageAction.Translate));
        if (Selecting)
        {
            Add("钉住", "钉住选区", () => Run(ImageAction.Pin));
            Add("长截图", "滚动长截图", OpenScroll);
            Add("录屏", "录制屏幕", OpenRecorder);
        }
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
                "录制屏幕" => keys.Record,
                "滚动长截图" => keys.LongShot,
                _ => null
            };
            b.ToolTip = string.IsNullOrEmpty(shortcut) ? name : $"{name}（{shortcut}）";
        };
        b.Click += (_, _) => action();
        buttons.Children.Add(b);
        return b;
    }

    private void BuildStyleControls()
    {
        foreach (var (name, value) in new (string Name, Color Color)[]
        {
            ("红", MarkStyle.Red), ("蓝", Color.FromRgb(25, 103, 186)), ("绿", Color.FromRgb(8, 121, 96)),
            ("黄", Color.FromRgb(248, 194, 38)), ("白", Colors.White), ("黑", Colors.Black)
        })
            color.Items.Add(new ComboBoxItem { Content = name, Tag = value, Foreground = new SolidColorBrush(value),
                Background = new SolidColorBrush(value == Colors.White ? Color.FromRgb(41, 54, 67) : Color.FromRgb(249, 251, 253)) });
        foreach (int option in new[] { 1, 2, 3, 4, 6, 8, 12, 16, 18, 20, 24, 28, 32, 40, 48, 64, 80 })
            size.Items.Add(option);
        font.Width = 108;
        foreach (var (control, name) in new (Control, string)[]
        {
            (number, "标注序号"), (color, "标注颜色"), (size, "标注大小"), (bold, "文字粗体"),
            (back, "文字背景"), (square, "方形序号"), (italic, "文字斜体"), (outline, "文字描边"), (font, "标注字体"),
            (numberNote, "编辑编号说明")
        })
        {
            AutomationProperties.SetName(control, name);
            control.ToolTip = name;
            control.Margin = new Thickness(3, 4, 4, 1);
        }
        styleControls.Children.Add(numberLabel);
        foreach (var control in new Control[] { number, color, size, font, bold, italic, outline, back, square, numberNote })
            styleControls.Children.Add(control);

        void ApplyStyle(object? _, RoutedEventArgs e)
        {
            if (updatingStyles || color.SelectedItem is not ComboBoxItem { Tag: Color selectedColor } || size.SelectedItem is not int selectedSize)
                return;
            var family = (font.SelectedItem as ComboBoxItem)?.Tag as string ?? MarkStyle.For(MarkKind.Text).Font;
            surface.SetStyle(new(selectedColor, selectedSize, bold.IsChecked == true, back.IsChecked == true,
                square.IsChecked == true, italic.IsChecked == true, outline.IsChecked == true, family));
        }
        color.SelectionChanged += ApplyStyle;
        size.SelectionChanged += ApplyStyle;
        font.SelectionChanged += ApplyStyle;
        bold.Click += ApplyStyle;
        italic.Click += ApplyStyle;
        outline.Click += ApplyStyle;
        back.Click += ApplyStyle;
        square.Click += ApplyStyle;
        numberNote.Click += (_, _) => surface.EditText();
        void SaveNumber()
        {
            if (int.TryParse(number.Text, out int value) && value is >= 1 and <= 9999)
                surface.SetNumber(value);
            else
                number.Text = (surface.Selected?.Number ?? surface.NextNumber).ToString(CultureInfo.InvariantCulture);
        }
        number.LostKeyboardFocus += (_, _) => SaveNumber();
        number.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            SaveNumber();
            surface.Focus();
            e.Handled = true;
        };
    }

    private string? AskText(string existing)
    {
        var input = new TextBox
        {
            Width = 340,
            MinHeight = 112,
            Margin = new Thickness(0, 0, 0, 10),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxLength = 4000,
            Text = existing
        };
        var body = new StackPanel
        {
            Margin = new Thickness(14)
        };
        body.Children.Add(input);
        var ok = new Button
        {
            Content = "确认文字",
            ToolTip = "Ctrl+Enter"
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
        dialog.SetResourceReference(ForegroundProperty, "Ink");
        AutomationProperties.SetName(input, "标注文字");
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                dialog.DialogResult = false;
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                dialog.DialogResult = true;
            }
        };
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void ToggleEditing()
    {
        editing = !editing;
        if (editing)
        {
            pinSize = new Size(Width, Height);
            var work = SystemParameters.WorkArea;
            double zoom = Math.Max(1, Math.Min(Math.Max(520 / Width, 210 / Height),
                Math.Min(work.Width * .85 / Width, work.Height * .85 / Height)));
            Width *= zoom;
            Height *= zoom;
            Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        }
        else if (pinSize is { } original)
        {
            Width = original.Width;
            Height = original.Height;
        }
        surface.UseTool(null);
        UpdateChrome();
        surface.Focus();
    }

    private void UpdateChrome()
    {
        surface.IsHitTestVisible = Selecting || editing;
        bool show = editing && surface.Selection.HasArea;
        toolbar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        info.Visibility = Selecting && show ? Visibility.Visible : Visibility.Collapsed;
        styleControls.MaxWidth = buttons.MaxWidth = Math.Max(1, ToolArea().Width - 8);
        undo.IsEnabled = surface.Document.CanUndo;
        redo.IsEnabled = surface.Document.CanRedo;
        foreach (var (kind, b) in marks)
        {
            b.SetResourceReference(Control.BackgroundProperty, surface.Tool == kind ? "Hot" : "Paper");
            AutomationProperties.SetItemStatus(b, surface.Tool == kind ? "已选中" : "");
        }
        UpdateStyleControls(show);
        if (desktop is not null)
        {
            var r = surface.Selection.Offset(desktop.Bounds.Left, desktop.Bounds.Top);
            dimensions.Text = $"{r.Left}, {r.Top}   ·   {r.Width} × {r.Height} px" +
                (recordingSelection ? "   ·   点击「录屏」继续" : "");
        }

        PositionBars();
    }

    private void UpdateStyleControls(bool show)
    {
        var style = surface.MarkAppearance;
        var kind = surface.Tool;
        bool isText = kind == MarkKind.Text, isNumber = kind == MarkKind.Number;
        styleControls.Visibility = show && kind is not null and not MarkKind.Mosaic ? Visibility.Visible : Visibility.Collapsed;
        numberLabel.Visibility = number.Visibility = square.Visibility = isNumber ? Visibility.Visible : Visibility.Collapsed;
        numberNote.Visibility = isNumber && surface.Selected is not null ? Visibility.Visible : Visibility.Collapsed;
        bold.Visibility = back.Visibility = italic.Visibility = outline.Visibility = font.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        updatingStyles = true;
        try
        {
            if (!number.IsKeyboardFocusWithin)
                number.Text = (surface.Selected?.Number ?? surface.NextNumber).ToString(CultureInfo.InvariantCulture);
            numberLabel.Text = surface.Selected is null ? "下一号" : "当前号";
            color.SelectedItem = color.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (Color)item.Tag == style.Color);
            size.SelectedItem = (int)style.Size;
            if (isText && font.Items.Count == 0)
            {
                font.Items.Add(new ComboBoxItem { Content = "系统字体", Tag = MarkStyle.For(MarkKind.Text).Font });
                foreach (var family in Fonts.SystemFontFamilies.OrderBy(x => x.Source, StringComparer.CurrentCultureIgnoreCase))
                    font.Items.Add(new ComboBoxItem { Content = family.Source, Tag = family.Source });
            }
            if (isText && !Equals((font.SelectedItem as ComboBoxItem)?.Tag, style.Font))
                font.SelectedItem = font.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, style.Font)) ?? font.Items[0];
            bold.IsChecked = style.Bold;
            back.IsChecked = style.Background;
            square.IsChecked = style.Square;
            italic.IsChecked = style.Italic;
            outline.IsChecked = style.Outline;
        }
        finally
        {
            updatingStyles = false;
        }
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

    private void OpenRecorder()
    {
        if (desktop is not null && surface.Selection.HasArea)
            tools.OpenRecorder(this, surface.Selection.Offset(desktop.Bounds.Left, desktop.Bounds.Top));
    }

    private void OpenScroll()
    {
        if (desktop is not null && surface.Selection.HasArea)
            tools.OpenScroll(this, surface.Selection.Offset(desktop.Bounds.Left, desktop.Bounds.Top));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (Keyboard.FocusedElement is TextBox || size.IsDropDownOpen || color.IsDropDownOpen || font.IsDropDownOpen)
            return;
        if (e.Key == Key.Escape)
        {
            if (!surface.ClearSelected())
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

        if (ShotChord.FromEvent(e, out var chord, allowUnmodified: Selecting))
        {
            var shortcuts = tools.Options.Shortcuts;
            foreach (var action in new[]
            {
                ShotAction.Pin,
                ShotAction.Ocr,
                ShotAction.Translate,
                ShotAction.Record,
                ShotAction.LongShot
            })
            {
                if (action is ShotAction.Pin or ShotAction.Record or ShotAction.LongShot && !Selecting)
                    continue;
                if (!ShotChord.TryParse(shortcuts[action], out var configured, CaptureOptions.AllowsSingleKey(action)) || configured != chord)
                    continue;
                if (action == ShotAction.Record)
                    OpenRecorder();
                else if (action == ShotAction.LongShot)
                    OpenScroll();
                else
                    Run(action == ShotAction.Pin ? ImageAction.Pin : action == ShotAction.Ocr ? ImageAction.Ocr : ImageAction.Translate);
                e.Handled = true;
                return;
            }
        }

        if (mods == ModifierKeys.Control && e.Key is Key.Z or Key.Y && editing)
            surface.History(e.Key == Key.Y);
        else if (editing && mods == ModifierKeys.None && e.Key == Key.Delete)
            surface.ClearSelected(true);
        else if (editing && surface.Selection.HasArea && mods == ModifierKeys.Shift && e.Key is Key.D4 or Key.D8)
            surface.UseTool(e.Key == Key.D4 ? MarkKind.Number : MarkKind.Text);
        else if (Selecting && e.Key == Key.A && (mods == ModifierKeys.Control || mods == (ModifierKeys.Control | ModifierKeys.Shift)))
            surface.SelectScreen(mods.HasFlag(ModifierKeys.Shift));
        else if (Selecting && recordingSelection && e.Key == Key.Enter && mods == ModifierKeys.None
            && Keyboard.FocusedElement is not Button)
            OpenRecorder();
        else if (Selecting && e.Key == Key.C && mods == ModifierKeys.None && surface.PickColor() is { } hex)
        {
            try
            {
                Clipboard.SetText(hex);
                dimensions.Text += $"   ·   已复制 {hex}";
            }
            catch (ExternalException)
            {
                dimensions.Text += "   ·   剪贴板正忙";
            }
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
