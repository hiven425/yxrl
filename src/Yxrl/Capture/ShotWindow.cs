using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
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
    private static readonly (string Name, Color Color)[] PaletteColors =
    [
        ("红", MarkStyle.Red),
        ("蓝", Color.FromRgb(25, 103, 186)),
        ("绿", Color.FromRgb(8, 121, 96)),
        ("黄", Color.FromRgb(248, 194, 38)),
        ("白", Colors.White),
        ("黑", Colors.Black)
    ];

    private DesktopFrame? desktop;
    private readonly bool recordingSelection;
    private readonly CaptureTools tools;
    private readonly ShotSurface surface;
    private readonly Grid root = new() { Background = Brushes.Transparent };
    private readonly Canvas chrome = new() { ClipToBounds = true };
    private readonly TextBlock dimensions = new() { Margin = new Thickness(6, 2, 6, 2), FontSize = 12 };
    private readonly Border info, toolbar, inlineHost, pinHudBorder;
    private readonly TextBlock pinHudText = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly TextBox inlineEditor = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Padding = new Thickness(4, 2, 4, 2),
        MinWidth = 76,
        MinHeight = 28,
        MaxLength = 4000
    };
    private readonly WrapPanel buttons = new(), styleControls = new();
    private readonly ComboBox size = new() { MinHeight = 26, Padding = new Thickness(7, 2, 22, 2) };
    private readonly ComboBox font = new() { Width = 104, MinHeight = 26, Padding = new Thickness(7, 2, 22, 2) };
    private readonly TextBox number = new() { Width = 48, MinHeight = 26, MaxLength = 4, Padding = new Thickness(5, 2, 5, 2) };
    private readonly CheckBox bold = new() { Content = "粗体", Margin = new Thickness(4, 0, 4, 0) };
    private readonly CheckBox back = new() { Content = "背景", Margin = new Thickness(4, 0, 4, 0) };
    private readonly CheckBox square = new() { Content = "方形", Margin = new Thickness(4, 0, 4, 0) };
    private readonly CheckBox italic = new() { Content = "斜体", Margin = new Thickness(4, 0, 4, 0) };
    private readonly CheckBox outline = new() { Content = "描边", Margin = new Thickness(4, 0, 4, 0) };
    private readonly Button numberNote = new() { Content = "编号说明", Padding = new Thickness(7, 2, 7, 2), MinHeight = 26 };
    private readonly TextBlock numberLabel = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly List<(Button Button, Color Color)> colorSwatches = [];
    private readonly Dictionary<MarkKind, Button> marks = new();
    private Button undo = null!, redo = null!, clearMarks = null!;
    private Button? refreshButton;

    private bool editing, updatingStyles, toolbarHidden;
    private int historyCursor = -1;
    private Size? pinSize;
    private Point inlineAnchor;
    private ShotMark? inlineTarget;

    // Pin interaction states
    private double pinOpacity;
    private double prePassthroughOpacity = 1.0;
    private bool isLocked, isPassthrough, thumbnailMode, suppressingContextMenu;
    private PixelRect? thumbRect, rightDragPreview;
    private Size preThumbSize;
    private Point? rightDragStart;
    private bool rightDragging;
    private string? borderResizeEdge;
    private PixelPoint resizeStartCursor;
    private Rect resizeStartRect;
    private double resizeAspect = 1;
    private Point? thumbPanStart;
    private PixelRect thumbPanOrigin;
    private DispatcherTimer? hudTimer, passthroughTimer;
    private Window? passthroughBar;
    private MenuItem? menuTopmost, menuLock, menuPassthrough, menuThumbnail;

    public bool Selecting => desktop is not null;
    internal double PinOpacity => pinOpacity;
    internal bool IsLocked => isLocked;
    internal bool IsPassthrough => isPassthrough;
    internal bool IsThumbnailMode => thumbnailMode;

    public ShotWindow(CaptureTools tools, ShotDocument document, DesktopFrame? desktop = null,
        PixelRect? placement = null, bool recordingSelection = false, double initialOpacity = 1.0)
    {
        this.tools = tools;
        this.desktop = desktop;
        this.recordingSelection = recordingSelection;
        pinOpacity = Math.Clamp(initialOpacity, 0.1, 1.0);
        editing = Selecting;
        Title = Selecting ? "YXRL 截图选区" : "YXRL 桌面贴图";
        AutomationProperties.SetAutomationId(this, Selecting ? "YxrlSelection" : "YxrlImagePin");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Background = Brushes.Black;

        Typography.SetNumeralAlignment(dimensions, FontNumeralAlignment.Tabular);
        surface = new ShotSurface(document, desktop)
        {
            EnterText = AskTextFallback
        };
        surface.InlineTextRequested += OpenInlineText;
        surface.SelectionDoubleClicked += () => Run(ImageAction.Copy);
        AutomationProperties.SetName(surface, Selecting ? "截图画布" : "贴图画布");

        AutomationProperties.SetName(inlineEditor, "标注文字");
        inlineEditor.PreviewKeyDown += InlineEditorKeyDown;
        inlineHost = new Border
        {
            Child = inlineEditor,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(24, 147, 238)),
            Visibility = Visibility.Collapsed,
            Effect = SoftShadow()
        };

        pinHudBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 22, 30, 38)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 4, 9, 4),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = pinHudText
        };
        Canvas.SetLeft(pinHudBorder, 8);
        Canvas.SetTop(pinHudBorder, 8);

        Content = root;
        root.Children.Add(surface);
        root.Children.Add(chrome);

        var infoRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        infoRow.Children.Add(dimensions);
        if (Selecting)
        {
            refreshButton = SmallBarButton("↻", "刷新画面", RefreshDesktop);
            infoRow.Children.Add(refreshButton);
        }
        info = Panel(infoRow);
        var toolsPanel = new StackPanel();
        toolsPanel.Children.Add(buttons);
        toolsPanel.Children.Add(styleControls);
        toolbar = Panel(toolsPanel);
        chrome.Children.Add(info);
        chrome.Children.Add(toolbar);
        chrome.Children.Add(inlineHost);
        chrome.Children.Add(pinHudBorder);

        BuildMainToolbar();
        BuildStyleControls();
        if (!Selecting)
            BuildPinContextMenu();

        var work = SystemParameters.WorkArea;
        double fit = Math.Min(1, Math.Min(work.Width * .85 / document.Width, work.Height * .85 / document.Height));
        Width = Math.Max(1, document.Width * fit);
        Height = Math.Max(1, document.Height * fit);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
        var initial = desktop?.Bounds ?? placement;
        if (initial is { } screen)
        {
            void Place()
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                Native.SetWindowPos(hwnd, Topmost ? Native.Topmost : Native.NoTopmost,
                    screen.Left, screen.Top, screen.Width, screen.Height, Native.NoOwnerZOrder);
                if (!Selecting)
                    Native.SetPinOverlayStyle(hwnd, pinOpacity, isPassthrough);
            }
            SourceInitialized += (_, _) => Place();
            Loaded += (_, _) => Place();
        }
        else if (!Selecting)
        {
            SourceInitialized += (_, _) => Native.SetPinOverlayStyle(new WindowInteropHelper(this).Handle, pinOpacity, isPassthrough);
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
        Closed += OnWindowClosed;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        root.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (inlineHost.Visibility == Visibility.Visible)
                CommitInlineText();
        };
        if (!Selecting)
            HookPinInteractions();
        else
            surface.MouseWheel += (_, e) =>
            {
                if (surface.Tool is null && surface.Selection.HasArea)
                {
                    surface.ScaleSelection(e.Delta);
                    e.Handled = true;
                }
            };

        UpdateChrome();
    }

    private static DropShadowEffect SoftShadow() => new()
    {
        BlurRadius = 14,
        ShadowDepth = 3,
        Opacity = 0.22,
        Color = Colors.Black
    };

    private static Border Panel(UIElement child)
    {
        var border = new Border
        {
            Child = child,
            Padding = new Thickness(5, 4, 5, 4),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Effect = SoftShadow()
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceGlass");
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        return border;
    }

    private static Button SmallBarButton(string glyph, string name, Action click)
    {
        var b = new Button
        {
            Content = glyph,
            ToolTip = $"{name}（F5）",
            Padding = new Thickness(5, 1, 5, 1),
            MinWidth = 24,
            MinHeight = 22,
            FontSize = 12
        };
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }

    private void BuildMainToolbar()
    {
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
        {
            marks[tool] = AddToolButton(sign, label, () =>
            {
                CommitInlineText();
                surface.UseTool(surface.Tool == tool ? null : tool);
                surface.Focus();
            });
        }

        AddSeparator();
        undo = AddToolButton("↶", "撤销", () =>
        {
            CommitInlineText();
            surface.History(false);
        });
        redo = AddToolButton("↷", "重做", () =>
        {
            CommitInlineText();
            surface.History(true);
        });
        clearMarks = AddToolButton("清空", "清空标注", () =>
        {
            CommitInlineText();
            surface.ClearAllMarks();
        });
        AddSeparator();
        AddToolButton("提取文字", "提取文字", () => Run(ImageAction.Ocr));
        AddToolButton("翻译", "打开翻译", () => Run(ImageAction.Translate));
        if (Selecting)
        {
            AddToolButton("钉住", "钉住选区", () => Run(ImageAction.Pin));
            AddToolButton("长截图", "滚动长截图", OpenScroll);
            AddToolButton("录屏", "录制屏幕", OpenRecorder);
        }
        AddToolButton("保存", "保存 PNG", () => Run(ImageAction.Save));
        AddToolButton("复制", "复制图片", () => Run(ImageAction.Copy));
        AddToolButton(Selecting ? "×" : "完成", Selecting ? "取消截图" : "完成标注", () =>
        {
            CommitInlineText();
            if (Selecting)
                Close();
            else
                ToggleEditing();
        });
    }

    private void AddSeparator()
    {
        var sep = new Border
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(4, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        sep.SetResourceReference(Border.BackgroundProperty, "Line");
        buttons.Children.Add(sep);
    }

    private Button AddToolButton(string text, string name, Action action)
    {
        var b = new Button
        {
            Content = text,
            ToolTip = name,
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(1, 0, 1, 0),
            MinWidth = 28,
            MinHeight = 28,
            FontSize = 12.5
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
                "清空标注" => "Ctrl+Shift+Z",
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
        var swatchPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 4, 6, 1)
        };
        foreach (var (name, value) in PaletteColors)
        {
            var dot = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(value),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(value == Colors.White ? Color.FromRgb(160, 175, 188) : Color.FromArgb(80, 0, 0, 0))
            };
            var btn = new Button
            {
                Content = dot,
                Width = 22,
                Height = 22,
                MinWidth = 22,
                MinHeight = 22,
                Padding = new Thickness(2),
                Margin = new Thickness(1, 0, 1, 0),
                BorderThickness = new Thickness(1.5),
                BorderBrush = Brushes.Transparent,
                ToolTip = name
            };
            AutomationProperties.SetName(btn, $"颜色：{name}");
            btn.Click += (_, _) => ApplyStyleChange(overrideColor: value);
            colorSwatches.Add((btn, value));
            swatchPanel.Children.Add(btn);
        }
        styleControls.Children.Add(swatchPanel);

        foreach (int option in new[] { 1, 2, 3, 4, 6, 8, 12, 16, 18, 20, 24, 28, 32, 40, 48, 64, 80 })
            size.Items.Add(option);
        foreach (var (control, name) in new (Control, string)[]
        {
            (number, "标注序号"), (size, "标注大小"), (bold, "文字粗体"),
            (back, "文字背景"), (square, "方形序号"), (italic, "文字斜体"),
            (outline, "文字描边"), (font, "标注字体"), (numberNote, "编辑编号说明")
        })
        {
            AutomationProperties.SetName(control, name);
            control.ToolTip = name;
            control.Margin = new Thickness(3, 4, 3, 1);
            control.VerticalAlignment = VerticalAlignment.Center;
        }
        styleControls.Children.Add(numberLabel);
        foreach (var control in new Control[] { number, size, font, bold, italic, outline, back, square, numberNote })
            styleControls.Children.Add(control);

        size.SelectionChanged += (_, _) => ApplyStyleChange();
        font.SelectionChanged += (_, _) => ApplyStyleChange();
        bold.Click += (_, _) => ApplyStyleChange();
        italic.Click += (_, _) => ApplyStyleChange();
        outline.Click += (_, _) => ApplyStyleChange();
        back.Click += (_, _) => ApplyStyleChange();
        square.Click += (_, _) => ApplyStyleChange();
        numberNote.Click += (_, _) =>
        {
            if (surface.Selected is { Kind: MarkKind.Number } sel)
                OpenInlineText(sel.Points[0], sel);
        };

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

    private void ApplyStyleChange(Color? overrideColor = null)
    {
        if (updatingStyles)
            return;
        Color selectedColor = overrideColor ?? surface.MarkAppearance.Color;
        int selectedSize = size.SelectedItem is int s ? s : (int)surface.MarkAppearance.Size;
        var family = (font.SelectedItem as ComboBoxItem)?.Tag as string ?? MarkStyle.For(MarkKind.Text).Font;
        var next = new MarkStyle(selectedColor, selectedSize, bold.IsChecked == true, back.IsChecked == true,
            square.IsChecked == true, italic.IsChecked == true, outline.IsChecked == true, family);
        surface.SetStyle(next);
        SyncInlineEditorStyle();
        if (overrideColor is not null && inlineHost.Visibility == Visibility.Visible)
            inlineEditor.Focus();
    }

    private void OpenInlineText(Point docPoint, ShotMark? existing)
    {
        CommitInlineText();
        inlineAnchor = existing?.Points[0] ?? docPoint;
        inlineTarget = existing;
        inlineEditor.Text = existing?.Text ?? "";
        SyncInlineEditorStyle();
        var viewPt = surface.ToView(inlineAnchor);
        var selRect = surface.ViewRect(surface.Selection);
        inlineHost.MaxWidth = Math.Max(100, selRect.Right - viewPt.X - 6);
        inlineHost.Visibility = Visibility.Visible;
        Canvas.SetLeft(inlineHost, Math.Clamp(viewPt.X, 4, Math.Max(4, ActualWidth - 130)));
        Canvas.SetTop(inlineHost, Math.Clamp(viewPt.Y, 4, Math.Max(4, ActualHeight - 44)));
        inlineEditor.Focus();
        inlineEditor.CaretIndex = inlineEditor.Text.Length;
    }

    private void SyncInlineEditorStyle()
    {
        if (inlineHost is null)
            return;
        var style = inlineTarget?.Appearance ?? surface.MarkAppearance;
        double scale = Math.Max(0.5, surface.ViewScaleX);
        inlineEditor.FontFamily = new FontFamily(style.Font);
        inlineEditor.FontSize = Math.Max(11, (inlineTarget?.Kind == MarkKind.Number ? Math.Max(10, Math.Round(style.Size * .55)) : style.Size) * scale);
        inlineEditor.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
        inlineEditor.FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal;
        inlineEditor.Foreground = new SolidColorBrush(style.Color);
        inlineEditor.CaretBrush = new SolidColorBrush(style.Color);
        bool bright = .2126 * style.Color.ScR + .7152 * style.Color.ScG + .0722 * style.Color.ScB > .45;
        inlineHost.Background = style.Background
            ? (bright ? new SolidColorBrush(Color.FromArgb(238, 22, 30, 38)) : new SolidColorBrush(Color.FromArgb(238, 255, 255, 255)))
            : (bright ? new SolidColorBrush(Color.FromArgb(200, 22, 30, 38)) : new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)));
    }

    private void CommitInlineText()
    {
        if (inlineHost.Visibility != Visibility.Visible)
            return;
        string value = inlineEditor.Text;
        var anchorPt = inlineAnchor;
        var targetMark = inlineTarget;
        CancelInlineText();
        surface.CommitInlineText(anchorPt, targetMark, value);
    }

    private void CancelInlineText()
    {
        inlineHost.Visibility = Visibility.Collapsed;
        inlineTarget = null;
        inlineEditor.Text = "";
        surface.Focus();
    }

    private void InlineEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelInlineText();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CommitInlineText();
            e.Handled = true;
        }
    }

    private string? AskTextFallback(string existing)
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
        var body = new StackPanel { Margin = new Thickness(14) };
        body.Children.Add(input);
        var ok = new Button { Content = "确认文字", ToolTip = "Ctrl+Enter" };
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
            if (e.Key == Key.Escape) dialog.DialogResult = false;
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                dialog.DialogResult = true;
            }
        };
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void BuildPinContextMenu()
    {
        ContextMenu = new ContextMenu();
        MenuItem Item(string title, Action action, bool checkable = false)
        {
            var item = new MenuItem { Header = title, IsCheckable = checkable };
            item.Click += (_, _) => action();
            ContextMenu.Items.Add(item);
            return item;
        }

        Item("标注", ToggleEditing);
        Item("提取文字", () => Run(ImageAction.Ocr));
        Item("翻译", () => Run(ImageAction.Translate));
        Item("复制图片", () => Run(ImageAction.Copy));
        Item("保存 PNG", () => Run(ImageAction.Save));
        ContextMenu.Items.Add(new Separator());
        menuTopmost = Item("窗口置顶 (T)", TogglePinTopmost, checkable: true);
        menuTopmost.IsChecked = Topmost;
        menuLock = Item("锁定贴图 (L)", TogglePinLock, checkable: true);
        menuPassthrough = Item("鼠标穿透", TogglePassthrough, checkable: true);
        menuThumbnail = Item("缩略图模式 (R)", ToggleThumbnailMode, checkable: true);
        var opacityMenu = new MenuItem { Header = "透明度" };
        foreach (int pct in new[] { 100, 80, 60, 40, 20 })
        {
            int captured = pct;
            var sub = new MenuItem { Header = $"{captured}%" };
            sub.Click += (_, _) =>
            {
                SetPinOpacity(captured / 100.0);
                ShowHud($"透明度 {captured}%");
            };
            opacityMenu.Items.Add(sub);
        }
        ContextMenu.Items.Add(opacityMenu);
        Item("还原原始尺寸", RestoreOriginalSize);
        ContextMenu.Items.Add(new Separator());
        Item("关闭贴图", Close);
        ContextMenuOpening += (_, e) =>
        {
            if (suppressingContextMenu)
            {
                suppressingContextMenu = false;
                e.Handled = true;
                return;
            }
            if (menuTopmost is not null) menuTopmost.IsChecked = Topmost;
            if (menuLock is not null) menuLock.IsChecked = isLocked;
            if (menuPassthrough is not null) menuPassthrough.IsChecked = isPassthrough;
            if (menuThumbnail is not null) menuThumbnail.IsChecked = thumbnailMode;
        };
    }

    private void HookPinInteractions()
    {
        root.PreviewMouseMove += OnPinMouseMove;
        root.PreviewMouseLeftButtonDown += OnPinLeftDown;
        root.PreviewMouseLeftButtonUp += OnPinLeftUp;
        root.PreviewMouseDown += OnPinAnyMouseDown;
        root.PreviewMouseRightButtonDown += OnPinRightDown;
        root.PreviewMouseRightButtonUp += OnPinRightUp;
        root.MouseWheel += OnPinWheel;
    }

    private string? BorderEdgeAt(Point pt)
    {
        if (editing || isLocked || isPassthrough)
            return null;
        const double edge = 6;
        bool l = pt.X <= edge, r = pt.X >= ActualWidth - edge, t = pt.Y <= edge, b = pt.Y >= ActualHeight - edge;
        if (!l && !r && !t && !b)
            return null;
        return (t ? "n" : b ? "s" : "") + (l ? "w" : r ? "e" : "");
    }

    private void OnPinMouseMove(object sender, MouseEventArgs e)
    {
        if (borderResizeEdge is { } edge && e.LeftButton == MouseButtonState.Pressed && Native.GetCursorPos(out var cur))
        {
            double scale = DpiScale();
            double dx = (cur.X - resizeStartCursor.X) / scale;
            double dy = (cur.Y - resizeStartCursor.Y) / scale;
            double nx = resizeStartRect.Left, ny = resizeStartRect.Top, nw = resizeStartRect.Width, nh = resizeStartRect.Height;
            if (edge.Contains('e')) nw = resizeStartRect.Width + dx;
            else if (edge.Contains('w')) { nw = resizeStartRect.Width - dx; nx = resizeStartRect.Right - nw; }
            if (edge.Contains('s')) nh = resizeStartRect.Height + dy;
            else if (edge.Contains('n')) { nh = resizeStartRect.Height - dy; ny = resizeStartRect.Bottom - nh; }

            if (edge.Length == 2 || edge is "w" or "e")
            {
                nw = Math.Max(32, nw);
                nh = Math.Max(32, nw / resizeAspect);
                if (edge.Contains('w')) nx = resizeStartRect.Right - nw;
                if (edge.Contains('n')) ny = resizeStartRect.Bottom - nh;
            }
            else
            {
                nh = Math.Max(32, nh);
                nw = Math.Max(32, nh * resizeAspect);
                if (edge.Contains('n')) ny = resizeStartRect.Bottom - nh;
            }
            Left = nx;
            Top = ny;
            Width = nw;
            Height = nh;
            ShowHud($"{Math.Round(Width * DpiScale() / ActiveContentPixelWidth() * 100):0}%");
            e.Handled = true;
            return;
        }

        if (thumbPanStart is { } panOrigin && thumbRect is { } tr && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(surface);
            double sx = surface.ActualWidth / Math.Max(1, tr.Width);
            double sy = surface.ActualHeight / Math.Max(1, tr.Height);
            int dx = (int)Math.Round((panOrigin.X - pos.X) / sx);
            int dy = (int)Math.Round((panOrigin.Y - pos.Y) / sy);
            var moved = ShotSelection.Move(thumbPanOrigin, dx, dy, surface.Document.Full);
            thumbRect = moved;
            surface.Viewport = moved;
            e.Handled = true;
            return;
        }

        if (rightDragStart is { } rStart && e.RightButton == MouseButtonState.Pressed)
        {
            var pt = e.GetPosition(surface);
            if (!rightDragging && (pt - rStart).Length > 6)
                rightDragging = true;
            if (rightDragging && thumbnailMode)
            {
                double sx = ActiveContentPixelWidth() / Math.Max(1, surface.ActualWidth);
                double sy = ActiveContentPixelHeight() / Math.Max(1, surface.ActualHeight);
                int ox = thumbRect?.Left ?? 0, oy = thumbRect?.Top ?? 0;
                var a = new Point(ox + rStart.X * sx, oy + rStart.Y * sy);
                var b = new Point(ox + pt.X * sx, oy + pt.Y * sy);
                rightDragPreview = ShotSelection.Between(a, b).Intersect(surface.Document.Full);
            }
            return;
        }

        if (!editing)
        {
            var hoverEdge = BorderEdgeAt(e.GetPosition(this));
            Cursor = hoverEdge switch
            {
                "nw" or "se" => Cursors.SizeNWSE,
                "ne" or "sw" => Cursors.SizeNESW,
                "n" or "s" => Cursors.SizeNS,
                "w" or "e" => Cursors.SizeWE,
                _ => Cursors.Arrow
            };
        }
    }

    private void OnPinLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (editing || isPassthrough)
            return;
        if (isLocked)
        {
            ShowHud("已锁定，右键解锁");
            e.Handled = true;
            return;
        }

        if (BorderEdgeAt(e.GetPosition(this)) is { } edge && Native.GetCursorPos(out var cur))
        {
            borderResizeEdge = edge;
            resizeStartCursor = cur;
            resizeStartRect = new Rect(Left, Top, Width, Height);
            resizeAspect = Width / Math.Max(1, Height);
            root.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (thumbnailMode && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && thumbRect is { } tr)
        {
            thumbPanStart = e.GetPosition(surface);
            thumbPanOrigin = tr;
            root.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                ToggleThumbnailMode();
            else
                Close();
            e.Handled = true;
            return;
        }

        DragMove();
        e.Handled = true;
    }

    private void OnPinLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (borderResizeEdge is not null || thumbPanStart is not null)
        {
            borderResizeEdge = null;
            thumbPanStart = null;
            if (root.IsMouseCaptured)
                root.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnPinAnyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (editing || isLocked || isPassthrough)
            return;
        if (e.ChangedButton == MouseButton.Middle)
        {
            RestoreOriginalSize();
            e.Handled = true;
        }
    }

    private void OnPinRightDown(object sender, MouseButtonEventArgs e)
    {
        if (!thumbnailMode || editing || isPassthrough)
            return;
        rightDragStart = e.GetPosition(surface);
        rightDragging = false;
        rightDragPreview = null;
    }

    private void OnPinRightUp(object sender, MouseButtonEventArgs e)
    {
        if (rightDragStart is null)
            return;
        bool dragged = rightDragging;
        var preview = rightDragPreview;
        rightDragStart = null;
        rightDragging = false;
        rightDragPreview = null;
        if (dragged)
        {
            suppressingContextMenu = true;
            if (preview is { Width: >= 16, Height: >= 16 } valid)
                ApplyThumbnailViewport(valid);
        }
    }

    private void OnPinWheel(object sender, MouseWheelEventArgs e)
    {
        if (editing || isPassthrough)
            return;
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control))
        {
            double delta = e.Delta > 0 ? 0.05 : -0.05;
            double next = Math.Clamp(Math.Round((pinOpacity + delta) * 20) / 20, 0.1, 1.0);
            SetPinOpacity(next);
            ShowHud($"透明度 {Math.Round(next * 100):0}%");
            e.Handled = true;
            return;
        }

        if (isLocked)
        {
            ShowHud("已锁定，右键解锁");
            return;
        }

        double step = mods.HasFlag(ModifierKeys.Alt) ? 0.01 : 0.10;
        double scale = DpiScale();
        double cw = ActiveContentPixelWidth() / scale;
        double ch = ActiveContentPixelHeight() / scale;
        double currentZoom = Width / Math.Max(1, cw);
        double nextZoom = Math.Clamp(currentZoom + (e.Delta > 0 ? step : -step), 0.1, 8.0);
        Width = Math.Max(20, cw * nextZoom);
        Height = Math.Max(20, ch * nextZoom);
        ShowHud($"{Math.Round(nextZoom * 100):0}%");
        e.Handled = true;
    }

    internal void SetPinOpacity(double value)
    {
        pinOpacity = Math.Clamp(value, 0.1, 1.0);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0)
            Native.SetPinOverlayStyle(hwnd, pinOpacity, isPassthrough);
    }

    internal void TogglePinTopmost()
    {
        Topmost = !Topmost;
        ShowInTaskbar = !Topmost;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0)
            Native.SetWindowPos(hwnd, Topmost ? Native.Topmost : Native.NoTopmost, 0, 0, 0, 0, Native.NoMove | Native.NoSize | Native.NoActivate | Native.NoOwnerZOrder);
        ShowHud(Topmost ? "已置顶" : "已取消置顶");
    }

    internal void TogglePinLock()
    {
        isLocked = !isLocked;
        ShowHud(isLocked ? "已锁定，右键解锁" : "已解锁");
    }

    internal void TogglePassthrough()
    {
        isPassthrough = !isPassthrough;
        if (isPassthrough)
        {
            prePassthroughOpacity = pinOpacity;
            if (pinOpacity > 0.50)
                SetPinOpacity(0.50);
            else
                SetPinOpacity(pinOpacity);
            StartPassthroughMonitor();
            ShowHud("已启用鼠标穿透");
        }
        else
        {
            StopPassthroughMonitor();
            SetPinOpacity(prePassthroughOpacity);
            ShowHud("已退出鼠标穿透");
        }
    }

    private void StartPassthroughMonitor()
    {
        passthroughTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        passthroughTimer.Tick += CheckPassthroughHover;
        passthroughTimer.Start();
        ShowPassthroughBar();
    }

    private void StopPassthroughMonitor()
    {
        if (passthroughTimer is not null)
        {
            passthroughTimer.Stop();
            passthroughTimer.Tick -= CheckPassthroughHover;
        }
        passthroughBar?.Close();
        passthroughBar = null;
    }

    private void ShowPassthroughBar()
    {
        if (passthroughBar is not null)
            return;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var grip = new Border
        {
            Padding = new Thickness(7, 3, 7, 3),
            Cursor = Cursors.SizeAll,
            Background = Brushes.Transparent,
            Child = new TextBlock { Text = "⋮ 拖动", VerticalAlignment = VerticalAlignment.Center, FontSize = 11.5 }
        };
        grip.MouseLeftButtonDown += (_, _) =>
        {
            passthroughBar?.DragMove();
            if (passthroughBar is { } bar)
            {
                Left = bar.Left;
                Top = bar.Top + bar.ActualHeight + 4;
            }
        };
        var exitBtn = new Button
        {
            Content = "退出鼠标穿透",
            Padding = new Thickness(8, 3, 8, 3),
            MinHeight = 24,
            FontSize = 11.5
        };
        exitBtn.Click += (_, _) =>
        {
            if (isPassthrough)
                TogglePassthrough();
        };
        panel.Children.Add(grip);
        panel.Children.Add(exitBtn);
        var host = new Border
        {
            Child = panel,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3)
        };
        host.SetResourceReference(Border.BackgroundProperty, "Paper");
        host.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        passthroughBar = new Window
        {
            Title = "退出鼠标穿透",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = host,
            Left = Left,
            Top = Math.Max(0, Top - 34)
        };
        passthroughBar.Show();
    }

    private void CheckPassthroughHover(object? sender, EventArgs e)
    {
        if (!isPassthrough || !Native.GetCursorPos(out var cur))
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var rect = Native.VisibleRect(hwnd);
        bool overPin = rect.HasArea && rect.Contains(cur);
        bool overBar = false;
        if (passthroughBar is not null)
        {
            var barRect = Native.VisibleRect(new WindowInteropHelper(passthroughBar).Handle);
            overBar = barRect.HasArea && barRect.Contains(cur);
        }
        if (overPin || overBar)
        {
            if (passthroughBar is null) ShowPassthroughBar();
            else if (!passthroughBar.IsVisible)
            {
                passthroughBar.Left = Left;
                passthroughBar.Top = Math.Max(0, Top - passthroughBar.ActualHeight - 4);
                passthroughBar.Show();
            }
        }
        else if (passthroughBar is { IsVisible: true })
        {
            passthroughBar.Hide();
        }
    }

    internal void ToggleThumbnailMode()
    {
        if (editing)
            return;
        if (!thumbnailMode)
        {
            preThumbSize = new Size(Width, Height);
            int w = surface.Document.Width, h = surface.Document.Height;
            var fallback = new PixelRect(w / 4, h / 4, Math.Max(w / 4 + 16, w * 3 / 4), Math.Max(h / 4 + 16, h * 3 / 4)).Intersect(surface.Document.Full);
            ApplyThumbnailViewport(thumbRect ?? fallback);
            ShowHud("缩略图模式（Shift+拖动平移 / 右键框选）");
        }
        else
        {
            thumbnailMode = false;
            surface.Viewport = null;
            if (preThumbSize.Width > 0 && preThumbSize.Height > 0)
            {
                Width = preThumbSize.Width;
                Height = preThumbSize.Height;
            }
            ShowHud("退出缩略图");
        }
    }

    private void ApplyThumbnailViewport(PixelRect viewport)
    {
        thumbnailMode = true;
        thumbRect = viewport;
        surface.Viewport = viewport;
        double scale = DpiScale();
        double targetW = Math.Clamp(viewport.Width / scale, 64, preThumbSize.Width > 0 ? preThumbSize.Width : Width);
        Width = targetW;
        Height = Math.Max(48, targetW * viewport.Height / Math.Max(1, viewport.Width));
    }

    internal void RestoreOriginalSize()
    {
        double scale = DpiScale();
        Width = Math.Max(20, ActiveContentPixelWidth() / scale);
        Height = Math.Max(20, ActiveContentPixelHeight() / scale);
        ShowHud("100%");
    }

    private int ActiveContentPixelWidth() => thumbRect is { } tr && thumbnailMode ? tr.Width : surface.Document.Width;
    private int ActiveContentPixelHeight() => thumbRect is { } tr && thumbnailMode ? tr.Height : surface.Document.Height;
    private double DpiScale()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        return hwnd == 0 ? 1.0 : Math.Max(1.0, Native.GetDpiForWindow(hwnd) / 96.0);
    }

    private void ShowHud(string text)
    {
        if (Selecting)
            return;
        pinHudText.Text = text;
        pinHudBorder.Visibility = Visibility.Visible;
        hudTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        hudTimer.Stop();
        hudTimer.Tick -= HideHud;
        hudTimer.Tick += HideHud;
        hudTimer.Start();
    }

    private void HideHud(object? sender, EventArgs e)
    {
        hudTimer?.Stop();
        pinHudBorder.Visibility = Visibility.Collapsed;
    }

    private void ToggleEditing()
    {
        CommitInlineText();
        editing = !editing;
        if (editing)
        {
            if (thumbnailMode)
                ToggleThumbnailMode();
            if (isPassthrough)
                TogglePassthrough();
            pinSize = new Size(Width, Height);
            var work = SystemParameters.WorkArea;
            double zoom = Math.Max(1, Math.Min(Math.Max(560 / Width, 240 / Height),
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

    internal void RefreshTheme()
    {
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        surface.IsHitTestVisible = Selecting || editing;
        bool show = !toolbarHidden && editing && surface.Selection.HasArea;
        toolbar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        info.Visibility = Selecting && show ? Visibility.Visible : Visibility.Collapsed;
        styleControls.MaxWidth = buttons.MaxWidth = Math.Max(1, ToolArea().Width - 12);
        undo.IsEnabled = surface.Document.CanUndo;
        redo.IsEnabled = surface.Document.CanRedo;
        clearMarks.IsEnabled = surface.Document.HasMarks;
        foreach (var (kind, b) in marks)
        {
            bool active = surface.Tool == kind;
            b.SetResourceReference(Control.BackgroundProperty, active ? "Hot" : "Paper");
            b.SetResourceReference(Control.ForegroundProperty, active ? "Accent" : "Ink");
            b.SetResourceReference(Control.BorderBrushProperty, active ? "Accent" : "Line");
            AutomationProperties.SetItemStatus(b, active ? "已选中" : "");
        }
        UpdateStyleControls(show);
        SyncInlineEditorStyle();
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
            foreach (var (btn, col) in colorSwatches)
            {
                bool match = col == style.Color;
                if (match)
                {
                    btn.SetResourceReference(Control.BorderBrushProperty, "Accent");
                    btn.SetResourceReference(Control.BackgroundProperty, "Hot");
                }
                else
                {
                    btn.BorderBrush = Brushes.Transparent;
                    btn.Background = Brushes.Transparent;
                }
            }
            if (!number.IsKeyboardFocusWithin)
                number.Text = (surface.Selected?.Number ?? surface.NextNumber).ToString(CultureInfo.InvariantCulture);
            numberLabel.Text = surface.Selected is null ? "下一号" : "当前号";
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
        CommitInlineText();
        if (!surface.Selection.HasArea)
            return;
        var rect = surface.Selection;
        var image = Selecting ? surface.Document.Crop(rect) : surface.Document;
        PixelRect? place = desktop is null ? null : rect.Offset(desktop.Bounds.Left, desktop.Bounds.Top);
        if (tools.Output(this, action, image, place) && Selecting)
            Close();
    }

    private void RefreshDesktop()
    {
        if (!Selecting)
            return;
        CommitInlineText();
        var savedSelection = surface.Selection;
        Hide();
        try
        {
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Thread.Sleep(45);
            var nextFrame = DesktopFrame.Freeze();
            desktop = nextFrame;
            surface.UpdateDesktop(nextFrame);
            if (savedSelection.HasArea)
                surface.SetSelection(savedSelection);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != 0)
                Native.SetWindowPos(hwnd, Native.Topmost, nextFrame.Bounds.Left, nextFrame.Bounds.Top,
                    nextFrame.Bounds.Width, nextFrame.Bounds.Height, Native.NoOwnerZOrder);
        }
        finally
        {
            Show();
            Activate();
            surface.Focus();
        }
    }

    private void RestoreSelectionHistory(bool newer)
    {
        if (desktop is null || tools.SelectionHistoryCount == 0)
            return;
        historyCursor = newer
            ? Math.Max(0, historyCursor - 1)
            : Math.Min(tools.SelectionHistoryCount - 1, historyCursor + 1);
        if (tools.GetSelectionHistory(historyCursor) is { } abs)
            surface.SetSelection(abs.Offset(-desktop.Bounds.Left, -desktop.Bounds.Top));
    }

    private void OpenRecorder()
    {
        CommitInlineText();
        if (desktop is not null && surface.Selection.HasArea)
            tools.OpenRecorder(this, surface.Selection.Offset(desktop.Bounds.Left, desktop.Bounds.Top));
    }

    private void OpenScroll()
    {
        CommitInlineText();
        if (desktop is not null && surface.Selection.HasArea)
            tools.OpenScroll(this, surface.Selection.Offset(desktop.Bounds.Left, desktop.Bounds.Top));
    }

    private static Key EffectiveKey(KeyEventArgs e) =>
        e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = EffectiveKey(e);
        if (Selecting && key is Key.LeftAlt or Key.RightAlt)
        {
            surface.ShowLoupe = true;
            return;
        }
        if (Selecting && surface.ShowLoupe && key is Key.LeftShift or Key.RightShift && !e.IsRepeat)
        {
            surface.HexColorFormat = !surface.HexColorFormat;
            return;
        }
        OnKey(sender, e);
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = EffectiveKey(e);
        if (Selecting && key is Key.LeftAlt or Key.RightAlt && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            surface.ShowLoupe = false;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = EffectiveKey(e);
        if (Keyboard.FocusedElement is TextBox || size.IsDropDownOpen || font.IsDropDownOpen)
            return;
        if (key == Key.Escape)
        {
            if (isLocked && !Selecting)
            {
                ShowHud("已锁定，右键解锁");
                e.Handled = true;
                return;
            }
            if (!surface.ClearSelected())
                Close();
            e.Handled = true;
            return;
        }

        if (key == Key.Space && mods == ModifierKeys.None)
        {
            if (Selecting)
            {
                toolbarHidden = !toolbarHidden;
                UpdateChrome();
            }
            else
                ToggleEditing();
            e.Handled = true;
            return;
        }

        if (!Selecting && !editing && mods == ModifierKeys.None)
        {
            if (key == Key.T) { TogglePinTopmost(); e.Handled = true; return; }
            if (key == Key.L) { TogglePinLock(); e.Handled = true; return; }
            if (key == Key.R) { ToggleThumbnailMode(); e.Handled = true; return; }
            if (!isLocked && key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var rect = Native.VisibleRect(hwnd);
                int dx = key == Key.Left ? -1 : key == Key.Right ? 1 : 0;
                int dy = key == Key.Up ? -1 : key == Key.Down ? 1 : 0;
                Native.SetWindowPos(hwnd, 0, rect.Left + dx, rect.Top + dy, 0, 0,
                    Native.NoSize | Native.NoZOrder | Native.NoActivate | Native.NoOwnerZOrder);
                e.Handled = true;
                return;
            }
        }

        if (Selecting && key == Key.F5 && mods == ModifierKeys.None)
        {
            RefreshDesktop();
            e.Handled = true;
            return;
        }

        if (Selecting && key == Key.R && mods is ModifierKeys.None or ModifierKeys.Shift)
        {
            RestoreSelectionHistory(mods == ModifierKeys.Shift);
            e.Handled = true;
            return;
        }

        if (Selecting && !surface.Selection.HasArea && key == Key.Enter && mods == ModifierKeys.None)
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

        if (editing && mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Z)
            surface.ClearAllMarks();
        else if (mods == ModifierKeys.Control && key is Key.Z or Key.Y && editing)
            surface.History(key == Key.Y);
        else if (editing && mods == ModifierKeys.None && key is Key.Delete or Key.Back)
            surface.ClearSelected(true);
        else if (editing && surface.Selection.HasArea && mods == ModifierKeys.Shift && key is Key.D4 or Key.D8)
            surface.UseTool(key == Key.D4 ? MarkKind.Number : MarkKind.Text);
        else if (Selecting && key == Key.A && (mods == ModifierKeys.Control || mods == (ModifierKeys.Control | ModifierKeys.Shift)))
            surface.SelectScreen(mods.HasFlag(ModifierKeys.Shift));
        else if (Selecting && recordingSelection && key == Key.Enter && mods == ModifierKeys.None
            && Keyboard.FocusedElement is not Button)
            OpenRecorder();
        else if (Selecting && key == Key.C && mods == ModifierKeys.None && surface.PickColor() is { } hex)
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
        else if ((key == Key.Enter && mods == ModifierKeys.None && Keyboard.FocusedElement is not Button) || (mods == ModifierKeys.Control && key == Key.C))
            Run(ImageAction.Copy);
        else if (mods == ModifierKeys.Control && key == Key.S)
            Run(ImageAction.Save);
        else if (Selecting && surface.Selection.HasArea && key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if (mods == ModifierKeys.None)
                surface.Nudge(key == Key.Left ? -1 : key == Key.Right ? 1 : 0, key == Key.Up ? -1 : key == Key.Down ? 1 : 0);
            else if (mods == ModifierKeys.Shift)
                surface.AdjustSelectionEdge(key == Key.Left ? 1 : 0, key == Key.Up ? 1 : 0, key == Key.Right ? -1 : 0, key == Key.Down ? -1 : 0);
            else if (mods == ModifierKeys.Control)
                surface.AdjustSelectionEdge(key == Key.Left ? -1 : 0, key == Key.Up ? -1 : 0, key == Key.Right ? 1 : 0, key == Key.Down ? 1 : 0);
            else
                return;
        }
        else
            return;
        e.Handled = true;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        hudTimer?.Stop();
        StopPassthroughMonitor();
        if (!Selecting)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var rect = hwnd != 0 ? Native.VisibleRect(hwnd) : default;
            if (!rect.HasArea)
            {
                double s = DpiScale();
                rect = new((int)Math.Round(Left * s), (int)Math.Round(Top * s),
                    (int)Math.Round((Left + Width) * s), (int)Math.Round((Top + Height) * s));
            }
            tools.RememberClosedPin(surface.Document, rect, isPassthrough ? prePassthroughOpacity : pinOpacity);
        }
    }
}
