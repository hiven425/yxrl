using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Yxrl.Desktop;
using Yxrl.Views;

namespace Yxrl.Capture;

internal sealed class TextToolWindow : Window
{
    private sealed class OcrImageCanvas : FrameworkElement
    {
        private static readonly Brush BoxFill = new SolidColorBrush(Color.FromArgb(20, 0, 95, 184));
        private static readonly Pen BoxPen = new(new SolidColorBrush(Color.FromArgb(105, 0, 95, 184)), 1);
        private static readonly Brush HoverBoxFill = new SolidColorBrush(Color.FromArgb(44, 24, 147, 238));
        private static readonly Pen HoverBoxPen = new(new SolidColorBrush(Color.FromArgb(165, 24, 147, 238)), 1.3);
        private static readonly Brush HotFill = new SolidColorBrush(Color.FromArgb(88, 24, 147, 238));
        private static readonly Pen HotPen = new(new SolidColorBrush(Color.FromRgb(24, 147, 238)), 1.75);
        private static readonly Brush MarqueeFill = new SolidColorBrush(Color.FromArgb(36, 24, 147, 238));
        static OcrImageCanvas()
        {
            BoxFill.Freeze(); BoxPen.Freeze();
            HoverBoxFill.Freeze(); HoverBoxPen.Freeze();
            HotFill.Freeze(); HotPen.Freeze(); MarqueeFill.Freeze();
        }

        private BitmapSource? image;
        private IReadOnlyList<OcrLine> lines = [];
        private HashSet<int> highlighted = [];
        private int hoverIndex = -1;
        private Point? dragStart;
        private Rect dragRect;
        public event Action<IReadOnlyList<int>>? LinesSelected;

        public void SetContent(BitmapSource? source, IReadOnlyList<OcrLine> ocrLines)
        {
            image = source;
            lines = ocrLines;
            highlighted.Clear();
            hoverIndex = -1;
            InvalidateVisual();
        }

        public void SetHighlights(IEnumerable<int> indices)
        {
            var next = new HashSet<int>(indices);
            if (highlighted.SetEquals(next)) return;
            highlighted = next;
            InvalidateVisual();
        }

        private Rect ImageRect()
        {
            if (image is null || ActualWidth <= 0 || ActualHeight <= 0 || image.PixelWidth <= 0 || image.PixelHeight <= 0)
                return Rect.Empty;
            double scale = Math.Min(ActualWidth / image.PixelWidth, ActualHeight / image.PixelHeight);
            double w = image.PixelWidth * scale, h = image.PixelHeight * scale;
            return new((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
        }

        private Point ViewToImage(Point view, Rect box) => image is null || box.IsEmpty
            ? default
            : new(Math.Clamp((view.X - box.Left) * image.PixelWidth / box.Width, 0, image.PixelWidth),
                  Math.Clamp((view.Y - box.Top) * image.PixelHeight / box.Height, 0, image.PixelHeight));

        private Rect ImageToView(Rect src, Rect box)
        {
            if (image is null || box.IsEmpty || src.IsEmpty) return Rect.Empty;
            double sx = box.Width / image.PixelWidth, sy = box.Height / image.PixelHeight;
            return new(box.Left + src.Left * sx, box.Top + src.Top * sy, src.Width * sx, src.Height * sy);
        }

        private int HitLineAt(Point view, Rect box)
        {
            if (image is null || box.IsEmpty || lines.Count == 0 || !box.Contains(view))
                return -1;
            var ip = ViewToImage(view, box);
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Bounds.Contains(ip))
                    return i;
            return -1;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var box = ImageRect();
            if (image is null || box.IsEmpty) return;
            dc.DrawImage(image, box);
            for (int i = 0; i < lines.Count; i++)
            {
                var vr = ImageToView(lines[i].Bounds, box);
                if (vr.IsEmpty) continue;
                bool active = highlighted.Contains(i);
                bool hover = !active && i == hoverIndex;
                dc.DrawRoundedRectangle(
                    active ? HotFill : hover ? HoverBoxFill : BoxFill,
                    active ? HotPen : hover ? HoverBoxPen : BoxPen,
                    vr, 3, 3);
            }
            if (dragStart is not null && !dragRect.IsEmpty)
                dc.DrawRoundedRectangle(MarqueeFill, HotPen, dragRect, 2, 2);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            var box = ImageRect();
            if (image is null || box.IsEmpty || lines.Count == 0) return;
            var pt = e.GetPosition(this);
            if (e.ClickCount == 2 && HitLineAt(pt, box) is >= 0 and int hit)
            {
                SetHighlights([hit]);
                LinesSelected?.Invoke([hit]);
                e.Handled = true;
                return;
            }
            dragStart = pt;
            dragRect = new(pt, pt);
            Cursor = Cursors.Cross;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var pt = e.GetPosition(this);
            if (dragStart is { } start)
            {
                dragRect = new(start, pt);
                InvalidateVisual();
                return;
            }
            var box = ImageRect();
            int hit = HitLineAt(pt, box);
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Cross;
            if (hit != hoverIndex)
            {
                hoverIndex = hit;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            if (dragStart is null && hoverIndex != -1)
            {
                hoverIndex = -1;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (dragStart is null) return;
            var box = ImageRect();
            var pt = e.GetPosition(this);
            var finalRect = new Rect(dragStart.Value, pt);
            dragStart = null;
            dragRect = Rect.Empty;
            if (IsMouseCaptured) ReleaseMouseCapture();
            var selected = new List<int>();
            if (!box.IsEmpty && finalRect.Width >= 4 && finalRect.Height >= 4)
            {
                var p1 = ViewToImage(finalRect.TopLeft, box);
                var p2 = ViewToImage(finalRect.BottomRight, box);
                var imgSel = new Rect(p1, p2);
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].Bounds.IntersectsWith(imgSel))
                        selected.Add(i);
            }
            else if (HitLineAt(pt, box) is >= 0 and int hit)
                selected.Add(hit);
            Cursor = HitLineAt(pt, box) >= 0 ? Cursors.Hand : Cursors.Cross;
            SetHighlights(selected);
            LinesSelected?.Invoke(selected);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private readonly TextBox source = Editor("原文"), result = Editor("译文");
    private readonly ComboBox language = new() { IsEditable = true, MinWidth = 112, MinHeight = 30, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox paragraphMode = new() { MinWidth = 98, MinHeight = 28, Padding = new Thickness(8, 3, 24, 3), Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox punctuationMode = new() { MinWidth = 90, MinHeight = 28, Padding = new Thickness(8, 3, 24, 3), Margin = new Thickness(0, 0, 8, 0) };
    private readonly CheckBox removeSpaces = new() { Content = "去空格", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox closeAfterCopy = new() { Content = "复制全部后关闭", Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton showOriginal, pinTopmost;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse statusDot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock leftBadge = CountText(), sourceBadge = CountText(), resultBadge = CountText();
    private readonly Border leftBadgePill = PillBorder(), sourceBadgePill = PillBorder(), resultBadgePill = PillBorder();
    private readonly TextBlock resultPlaceholder = new()
    {
        Text = "选择目标语言后点击「翻译」，优先翻译当前选中内容",
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(11, 8, 11, 8),
        IsHitTestVisible = false
    };
    private readonly OcrImageCanvas imageCanvas = new() { MinHeight = 160, Cursor = Cursors.Cross };
    private readonly Grid columns = new();
    private readonly Border leftCard, rightCard, topBar;
    private readonly Button translate, recognize, copySource, copyResult;
    private readonly BitmapSource? displayInput, ocrInput;
    private readonly CaptureTools tools;
    private readonly CancellationTokenSource life = new();
    private OcrDocument? document;
    private IReadOnlyList<OcrSpan> currentSpans = [];
    private string formattedText = "";
    private bool busy, syncingSelection, micaEnabled;
    private int selectedLineCount;

    public TextToolWindow(CaptureTools tools, ShotDocument? image, bool translation)
    {
        this.tools = tools;
        Title = image is null ? "YXRL 粘贴文字翻译" : translation ? "YXRL 截图翻译" : "YXRL 提取文字";
        if (Theme.AppIcon is { } appIcon) Icon = appIcon;
        Width = image is null ? 560 : 900;
        Height = image is null ? 540 : 600;
        MinWidth = 420;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetAutomationId(this, "YxrlCaptureText");

        showOriginal = IconPillToggle("\uE8B9", "原图", "显示原图", checkedByDefault: true);
        pinTopmost = IconPillToggle("\uE718", "置顶", "置顶", checkedByDefault: false);

        var root = new DockPanel { Margin = new Thickness(14, 12, 14, 12) };
        Content = root;

        // Top Fluent command bar
        var topDock = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var rightControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (image is not null)
            rightControls.Children.Add(showOriginal);
        pinTopmost.Margin = new Thickness(image is not null ? 6 : 0, 0, 0, 0);
        rightControls.Children.Add(pinTopmost);
        DockPanel.SetDock(rightControls, Dock.Right);
        topDock.Children.Add(rightControls);

        var leftControls = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        if (image is not null)
        {
            foreach (var item in new[] { "智能分段", "合并换行", "保留换行" }) paragraphMode.Items.Add(item);
            paragraphMode.SelectedIndex = 0;
            AutomationProperties.SetName(paragraphMode, "段落模式");
            foreach (var item in new[] { "原标点", "中文标点", "英文标点" }) punctuationMode.Items.Add(item);
            punctuationMode.SelectedIndex = 0;
            AutomationProperties.SetName(punctuationMode, "标点转换");

            leftControls.Children.Add(MutedLabel("排版"));
            leftControls.Children.Add(paragraphMode);
            leftControls.Children.Add(punctuationMode);
            leftControls.Children.Add(removeSpaces);

            var divider = new Border { Width = 1, Height = 18, Margin = new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            divider.SetResourceReference(Border.BackgroundProperty, "Line");
            leftControls.Children.Add(divider);

            leftControls.Children.Add(MutedLabel("字号"));
            var segHost = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            segHost.SetResourceReference(Border.BackgroundProperty, "Card");
            segHost.SetResourceReference(Border.BorderBrushProperty, "Line");
            var segWrap = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (lbl, sz) in new[] { ("小", 12.0), ("中", 14.0), ("大", 16.5) })
            {
                var radio = new RadioButton
                {
                    Content = lbl,
                    GroupName = "OcrFontSize",
                    IsChecked = Math.Abs(sz - 14.0) < 0.1
                };
                radio.SetResourceReference(StyleProperty, "Segment");
                double fontSize = sz;
                radio.Checked += (_, _) =>
                {
                    source.FontSize = fontSize;
                    result.FontSize = fontSize;
                };
                segWrap.Children.Add(radio);
            }
            segHost.Child = segWrap;
            leftControls.Children.Add(segHost);
        }
        else
        {
            leftControls.Children.Add(new TextBlock
            {
                Text = "粘贴或输入文本，使用已配置的模型接口进行翻译",
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            ((TextBlock)leftControls.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        }
        topDock.Children.Add(leftControls);
        topBar = CardBorder(topDock, padding: new Thickness(10, 7, 10, 7));
        topBar.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        // Bottom status bar with semantic dot and behavior options
        var bottomBar = new DockPanel { Margin = new Thickness(2, 9, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        var bottomRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (image is not null)
            bottomRight.Children.Add(closeAfterCopy);
        var escHint = new TextBlock { Text = "Esc 关闭", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        escHint.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        bottomRight.Children.Add(escHint);
        DockPanel.SetDock(bottomRight, Dock.Right);
        bottomBar.Children.Add(bottomRight);

        var statusGroup = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(statusDot, Dock.Left);
        statusDot.Margin = new Thickness(0, 0, 7, 0);
        statusGroup.Children.Add(statusDot);
        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        statusGroup.Children.Add(status);
        bottomBar.Children.Add(statusGroup);
        DockPanel.SetDock(bottomBar, Dock.Bottom);
        root.Children.Add(bottomBar);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        SetStatusState("正在准备…", "Muted");

        // Two-column workspace
        columns.ColumnDefinitions.Add(new());
        columns.ColumnDefinitions.Add(new());
        columns.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto });

        // Left Card: Original image stage + count pill + subtle footer hint
        leftBadgePill.Child = leftBadge;
        leftBadgePill.Visibility = Visibility.Collapsed;
        var leftStack = new DockPanel();
        var leftHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(leftBadgePill, Dock.Right);
        leftHeader.Children.Add(leftBadgePill);
        leftHeader.Children.Add(Label("识别原图", margin: new Thickness(0)));
        DockPanel.SetDock(leftHeader, Dock.Top);
        leftStack.Children.Add(leftHeader);

        var leftTip = new TextBlock
        {
            Text = "拖框或双击文字块可同步右侧选区",
            FontSize = 11.5,
            Margin = new Thickness(0, 7, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        leftTip.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        DockPanel.SetDock(leftTip, Dock.Bottom);
        leftStack.Children.Add(leftTip);

        var imageStage = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = imageCanvas
        };
        imageStage.SetResourceReference(Border.BackgroundProperty, "Card");
        imageStage.SetResourceReference(Border.BorderBrushProperty, "Line");
        leftStack.Children.Add(imageStage);
        leftCard = CardBorder(leftStack);

        // Right Card: Flexible 2-stage editor + translation bridge strip
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 96 });
        rightGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new() { Height = new GridLength(0.92, GridUnitType.Star), MinHeight = 90 });

        // Row 0: Source text section
        var srcPane = new DockPanel();
        var srcHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };
        var srcActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        recognize = CompactActionButton("重新提取", () => _ = Recognize());
        copySource = CompactActionButton("复制原文", () => Copy(source, allowCloseAll: true), margin: new Thickness(0));
        srcActions.Children.Add(recognize);
        srcActions.Children.Add(copySource);
        DockPanel.SetDock(srcActions, Dock.Right);
        srcHeader.Children.Add(srcActions);

        var srcTitleGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        srcTitleGroup.Children.Add(Label(image is null ? "原文" : "识别与校对原文", margin: new Thickness(0)));
        sourceBadgePill.Child = sourceBadge;
        sourceBadgePill.Margin = new Thickness(8, 0, 0, 0);
        srcTitleGroup.Children.Add(sourceBadgePill);
        srcHeader.Children.Add(srcTitleGroup);
        DockPanel.SetDock(srcHeader, Dock.Top);
        srcPane.Children.Add(srcHeader);
        srcPane.Children.Add(source);
        Grid.SetRow(srcPane, 0);
        rightGrid.Children.Add(srcPane);

        // Row 1: Translation bridge strip
        foreach (var preset in TranslationOptions.Languages)
            language.Items.Add(preset);
        language.Text = tools.Translation.TargetLanguage;
        AutomationProperties.SetName(language, "目标语言");
        translate = CompactActionButton("翻译", () => _ = Translate(), primary: true, margin: new Thickness(0));
        translate.Padding = new Thickness(16, 4, 16, 4);
        translate.MinHeight = 30;
        var openSettings = CompactActionButton("翻译设置", tools.OpenSettings, margin: new Thickness(8, 0, 0, 0));

        var bridgeDock = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(openSettings, Dock.Right);
        bridgeDock.Children.Add(openSettings);
        var bridgeLeft = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        bridgeLeft.Children.Add(MutedLabel("目标语言"));
        bridgeLeft.Children.Add(language);
        bridgeLeft.Children.Add(translate);
        bridgeDock.Children.Add(bridgeLeft);

        var bridgeStrip = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 10, 0, 10),
            Child = bridgeDock
        };
        bridgeStrip.SetResourceReference(Border.BackgroundProperty, "Card");
        bridgeStrip.SetResourceReference(Border.BorderBrushProperty, "Line");
        Grid.SetRow(bridgeStrip, 1);
        rightGrid.Children.Add(bridgeStrip);

        // Row 2: Translation output section
        var dstPane = new DockPanel();
        var dstHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };
        copyResult = CompactActionButton("复制译文", () => Copy(result, allowCloseAll: false), margin: new Thickness(0));
        copyResult.IsEnabled = false;
        DockPanel.SetDock(copyResult, Dock.Right);
        dstHeader.Children.Add(copyResult);

        var dstTitleGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        dstTitleGroup.Children.Add(Label("译文", margin: new Thickness(0)));
        resultBadgePill.Child = resultBadge;
        resultBadgePill.Margin = new Thickness(8, 0, 0, 0);
        dstTitleGroup.Children.Add(resultBadgePill);
        dstHeader.Children.Add(dstTitleGroup);
        DockPanel.SetDock(dstHeader, Dock.Top);
        dstPane.Children.Add(dstHeader);

        result.IsReadOnly = true;
        result.SetResourceReference(BackgroundProperty, "Surface");
        resultPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        var resultHost = new Grid();
        resultHost.Children.Add(result);
        resultHost.Children.Add(resultPlaceholder);
        dstPane.Children.Add(resultHost);
        Grid.SetRow(dstPane, 2);
        rightGrid.Children.Add(dstPane);

        rightCard = CardBorder(rightGrid);
        columns.Children.Add(leftCard);
        columns.Children.Add(rightCard);
        root.Children.Add(columns);

        if (image is null)
        {
            leftCard.Visibility = Visibility.Collapsed;
            recognize.Visibility = Visibility.Collapsed;
            SetStatusState("请输入或粘贴需要翻译的文字，也可按选段翻译。", "Muted");
        }
        else
        {
            displayInput = image.Render();
            ocrInput = image.Render(forOcr: true);
            imageCanvas.SetContent(displayInput, []);
        }

        paragraphMode.SelectionChanged += (_, _) => ReformatFromCache();
        punctuationMode.SelectionChanged += (_, _) => ReformatFromCache();
        removeSpaces.Click += (_, _) => ReformatFromCache();
        showOriginal.Click += (_, _) => LayoutColumns();
        pinTopmost.Click += (_, _) => Topmost = pinTopmost.IsChecked == true;
        source.TextChanged += (_, _) =>
        {
            result.Clear();
            UpdateCounts();
        };
        source.SelectionChanged += SyncTextSelectionToImage;
        result.TextChanged += (_, _) => UpdateCounts();
        imageCanvas.LinesSelected += SyncImageSelectionToText;

        SizeChanged += (_, _) => LayoutColumns();
        Loaded += async (_, _) =>
        {
            LayoutColumns();
            UpdateCounts();
            if (ocrInput is not null)
                await Recognize();
            else
                source.Focus();
        };
        Closed += (_, _) =>
        {
            life.Cancel();
            life.Dispose();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyChrome();
    }

    internal void RefreshTheme() => ApplyChrome();

    private void ApplyChrome()
    {
        micaEnabled = Theme.ApplyWindowChrome(this, Native.BackdropKind.Mica);
        if (micaEnabled) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "Surface");
    }

    private void LayoutColumns()
    {
        bool showLeft = ocrInput is not null && showOriginal.IsChecked == true;
        leftCard.Visibility = showLeft ? Visibility.Visible : Visibility.Collapsed;
        bool split = showLeft && ActualWidth >= 660;
        Grid.SetColumn(rightCard, split ? 1 : 0);
        Grid.SetRow(rightCard, showLeft && !split ? 1 : 0);
        columns.ColumnDefinitions[0].Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        columns.ColumnDefinitions[1].Width = split ? new GridLength(1.18, GridUnitType.Star) : new GridLength(0);
        columns.RowDefinitions[0].Height = split ? new GridLength(1, GridUnitType.Star) : showLeft ? new GridLength(0.75, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        columns.RowDefinitions[1].Height = split ? new GridLength(0) : showLeft ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        rightCard.Margin = split ? new Thickness(10, 0, 0, 0) : showLeft ? new Thickness(0, 10, 0, 0) : new Thickness(0);
    }

    private static ToggleButton IconPillToggle(string glyph, string text, string name, bool checkedByDefault)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var btn = new ToggleButton
        {
            Content = panel,
            IsChecked = checkedByDefault,
            ToolTip = name
        };
        btn.SetResourceReference(StyleProperty, "PillToggle");
        AutomationProperties.SetName(btn, name);
        return btn;
    }

    private static Border PillBorder()
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1.5, 8, 1.5),
            VerticalAlignment = VerticalAlignment.Center
        };
        border.SetResourceReference(Border.BackgroundProperty, "Card");
        return border;
    }

    private static TextBlock CountText()
    {
        var block = new TextBlock { FontSize = 11, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    private static Border CardBorder(UIElement child, Thickness? padding = null)
    {
        var border = new Border
        {
            Child = child,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(12, 10, 12, 10)
        };
        border.SetResourceReference(Border.BackgroundProperty, "Paper");
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }

    private static TextBlock Label(string name, Thickness? margin = null) => new()
    {
        Text = name,
        FontWeight = FontWeights.SemiBold,
        FontSize = 13.5,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = margin ?? new Thickness(0, 0, 0, 6)
    };

    private static TextBlock MutedLabel(string name)
    {
        var t = new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return t;
    }

    private static TextBox Editor(string name)
    {
        var field = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(field, name);
        return field;
    }

    private Button CompactActionButton(string label, Action action, bool primary = false, Thickness? margin = null)
    {
        var button = new Button
        {
            Content = label,
            Margin = margin ?? new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 3, 10, 3),
            MinHeight = 27,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        button.SetResourceReference(StyleProperty, primary ? "PrimaryButton" : "SecondaryButton");
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateCounts()
    {
        int srcLen = source.Text.Length;
        int selLen = source.SelectionLength;
        sourceBadge.Text = selLen > 0 ? $"已选 {selLen} 字" : $"{srcLen} 字";
        sourceBadgePill.Visibility = srcLen > 0 ? Visibility.Visible : Visibility.Collapsed;
        copySource.IsEnabled = !busy && srcLen > 0;

        int dstLen = result.Text.Length;
        resultBadge.Text = $"{dstLen} 字";
        resultBadgePill.Visibility = dstLen > 0 ? Visibility.Visible : Visibility.Collapsed;
        resultPlaceholder.Visibility = dstLen == 0 ? Visibility.Visible : Visibility.Collapsed;
        copyResult.IsEnabled = !busy && dstLen > 0;

        int totalLines = document?.Lines.Count ?? 0;
        leftBadge.Text = selectedLineCount > 0 ? $"已选 {selectedLineCount} / {totalLines}" : $"{totalLines} 处文本";
        leftBadgePill.Visibility = totalLines > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatusState(string message, string dotBrushKey)
    {
        status.Text = message;
        statusDot.SetResourceReference(Shape.FillProperty, dotBrushKey);
    }

    private OcrLayoutOptions CurrentLayoutOptions() => new(
        (OcrParagraphMode)Math.Clamp(paragraphMode.SelectedIndex, 0, 2),
        removeSpaces.IsChecked == true,
        (OcrPunctuationMode)Math.Clamp(punctuationMode.SelectedIndex, 0, 2));

    private void ReformatFromCache()
    {
        if (document is null) return;
        var formatted = OcrLayout.Format(document, CurrentLayoutOptions());
        currentSpans = formatted.Spans;
        formattedText = formatted.Text;
        selectedLineCount = 0;
        syncingSelection = true;
        try
        {
            source.SelectAll();
            source.SelectedText = formatted.Text;
            source.Select(0, 0);
        }
        finally
        {
            syncingSelection = false;
        }
        imageCanvas.SetHighlights([]);
        UpdateCounts();
    }

    private void SyncTextSelectionToImage(object sender, RoutedEventArgs e)
    {
        if (syncingSelection)
            return;
        if (currentSpans.Count == 0 || source.Text != formattedText)
        {
            UpdateCounts();
            return;
        }
        int start = source.SelectionStart, length = source.SelectionLength;
        if (length <= 0)
        {
            selectedLineCount = 0;
            imageCanvas.SetHighlights([]);
            UpdateCounts();
            return;
        }
        int end = start + length;
        var hits = new List<int>();
        foreach (var span in currentSpans)
            if (span.Length > 0 && span.Start < end && span.Start + span.Length > start)
                hits.Add(span.LineIndex);
        selectedLineCount = hits.Distinct().Count();
        imageCanvas.SetHighlights(hits);
        UpdateCounts();
    }

    private void SyncImageSelectionToText(IReadOnlyList<int> lineIndices)
    {
        if (currentSpans.Count == 0 || source.Text != formattedText)
            return;
        var matching = currentSpans.Where(s => lineIndices.Contains(s.LineIndex) && s.Length > 0).ToArray();
        if (matching.Length == 0)
            return;
        selectedLineCount = matching.Select(s => s.LineIndex).Distinct().Count();
        int minStart = matching.Min(s => s.Start);
        int maxEnd = matching.Max(s => s.Start + s.Length);
        syncingSelection = true;
        try
        {
            source.Focus();
            source.Select(minStart, Math.Max(0, maxEnd - minStart));
        }
        finally
        {
            syncingSelection = false;
        }
        UpdateCounts();
    }

    private void Copy(TextBox editor, bool allowCloseAll)
    {
        bool all = editor.SelectionLength == 0;
        var text = all ? editor.Text : editor.SelectedText;
        if (string.IsNullOrEmpty(text))
        {
            SetStatusState("没有可复制的文字。", "Muted");
            return;
        }

        try
        {
            Clipboard.SetText(text);
            SetStatusState("已复制文字到剪贴板。", "Positive");
            if (allowCloseAll && all && closeAfterCopy.IsChecked == true)
                Close();
        }
        catch (COMException)
        {
            SetStatusState("剪贴板正在使用，请重试。", "Danger");
        }
    }

    private void SetBusy(bool value, string message, string dotBrushKey = "Accent")
    {
        busy = value;
        translate.IsEnabled = recognize.IsEnabled = !value;
        source.IsReadOnly = value;
        SetStatusState(message, dotBrushKey);
        UpdateCounts();
    }

    private async Task Recognize()
    {
        if (busy || ocrInput is null)
            return;
        var token = life.Token;
        SetBusy(true, "正在本机识别文字…", "Accent");
        try
        {
            var doc = await tools.Ocr.ReadDocumentAsync(ocrInput, token);
            if (token.IsCancellationRequested)
                return;
            document = doc;
            selectedLineCount = 0;
            imageCanvas.SetContent(displayInput, doc.Lines);
            var formatted = OcrLayout.Format(doc, CurrentLayoutOptions());
            currentSpans = formatted.Spans;
            formattedText = formatted.Text;
            syncingSelection = true;
            try { source.Text = formatted.Text; }
            finally { syncingSelection = false; }
            result.Clear();
            SetBusy(false, doc.Lines.Count == 0 ? "没有识别到文字，可重新框选或手动输入。" : $"识别完成（{doc.Lines.Count} 段），可在左图划选或在右侧校对。", doc.Lines.Count == 0 ? "Muted" : "Positive");
            source.Focus();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or TypeInitializationException or DllNotFoundException or ArgumentException)
        {
            if (!token.IsCancellationRequested)
                SetBusy(false, "本机 OCR 失败，可重试。" + ex.GetBaseException().Message, "Danger");
        }
    }

    private async Task Translate()
    {
        if (busy)
            return;
        var text = source.SelectionLength > 0 ? source.SelectedText : source.Text;
        var token = life.Token;
        result.Clear();
        SetBusy(true, "正在翻译…", "Accent");
        try
        {
            var options = tools.Translation;
            var endpoint = options.Endpoint() ?? throw new FormatException("请先打开「翻译设置」，填写 API 基础地址和模型名。");
            var translated = await tools.Translator.Translate(options, text, language.Text, TranslationCredential.Read(endpoint), token);
            if (!token.IsCancellationRequested)
            {
                result.Text = translated;
                SetBusy(false, "翻译完成。", "Positive");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or OperationCanceledException or IOException or JsonException or Win32Exception or InvalidDataException)
        {
            if (!token.IsCancellationRequested)
                SetBusy(false, ex is OperationCanceledException ? "翻译超时，请重试。" : ex is JsonException ? "翻译服务返回了无法识别的响应。" : ex.Message, "Danger");
        }
    }
}
