using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yxrl.Desktop;
using Yxrl.Views;

namespace Yxrl.Capture;

internal sealed class TextToolWindow : Window
{
    private sealed class OcrImageCanvas : FrameworkElement
    {
        private static readonly Brush BoxFill = new SolidColorBrush(Color.FromArgb(24, 0, 95, 184));
        private static readonly Pen BoxPen = new(new SolidColorBrush(Color.FromArgb(110, 0, 95, 184)), 1);
        private static readonly Brush HotFill = new SolidColorBrush(Color.FromArgb(88, 24, 147, 238));
        private static readonly Pen HotPen = new(new SolidColorBrush(Color.FromRgb(24, 147, 238)), 1.75);
        private static readonly Brush MarqueeFill = new SolidColorBrush(Color.FromArgb(40, 24, 147, 238));
        static OcrImageCanvas()
        {
            BoxFill.Freeze(); BoxPen.Freeze(); HotFill.Freeze(); HotPen.Freeze(); MarqueeFill.Freeze();
        }

        private BitmapSource? image;
        private IReadOnlyList<OcrLine> lines = [];
        private HashSet<int> highlighted = [];
        private Point? dragStart;
        private Rect dragRect;
        public event Action<IReadOnlyList<int>>? LinesSelected;

        public void SetContent(BitmapSource? source, IReadOnlyList<OcrLine> ocrLines)
        {
            image = source;
            lines = ocrLines;
            highlighted.Clear();
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
                dc.DrawRectangle(active ? HotFill : BoxFill, active ? HotPen : BoxPen, vr);
            }
            if (dragStart is not null && !dragRect.IsEmpty)
                dc.DrawRectangle(MarqueeFill, HotPen, dragRect);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            var box = ImageRect();
            if (image is null || box.IsEmpty || lines.Count == 0) return;
            var pt = e.GetPosition(this);
            if (e.ClickCount == 2)
            {
                var ip = ViewToImage(pt, box);
                int hit = -1;
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].Bounds.Contains(ip)) { hit = i; break; }
                if (hit >= 0)
                {
                    SetHighlights([hit]);
                    LinesSelected?.Invoke([hit]);
                    e.Handled = true;
                    return;
                }
            }
            dragStart = pt;
            dragRect = new(pt, pt);
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragStart is not { } start) return;
            dragRect = new(start, e.GetPosition(this));
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (dragStart is null) return;
            var box = ImageRect();
            var finalRect = new Rect(dragStart.Value, e.GetPosition(this));
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
            else if (!box.IsEmpty)
            {
                var ip = ViewToImage(e.GetPosition(this), box);
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].Bounds.Contains(ip)) { selected.Add(i); break; }
            }
            SetHighlights(selected);
            LinesSelected?.Invoke(selected);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private readonly TextBox source = Editor("原文"), result = Editor("译文");
    private readonly ComboBox language = new() { IsEditable = true, MinWidth = 112, Margin = new Thickness(4, 0, 6, 0) };
    private readonly ComboBox paragraphMode = new() { MinWidth = 104, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox punctuationMode = new() { MinWidth = 96, Margin = new Thickness(0, 0, 6, 0) };
    private readonly CheckBox removeSpaces = new() { Content = "去空格", Margin = new Thickness(2, 0, 8, 0) };
    private readonly CheckBox showOriginal = new() { Content = "显示原图", IsChecked = true, Margin = new Thickness(2, 0, 8, 0) };
    private readonly CheckBox pinTopmost = new() { Content = "置顶", Margin = new Thickness(2, 0, 8, 0) };
    private readonly CheckBox closeAfterCopy = new() { Content = "复制全部后关闭", Margin = new Thickness(2, 0, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly OcrImageCanvas imageCanvas = new() { MinHeight = 200, Cursor = Cursors.IBeam };
    private readonly Grid columns = new();
    private readonly Border leftCard, rightCard, topBar;
    private readonly Button translate, recognize;
    private readonly BitmapSource? displayInput, ocrInput;
    private readonly CaptureTools tools;
    private readonly CancellationTokenSource life = new();
    private OcrDocument? document;
    private IReadOnlyList<OcrSpan> currentSpans = [];
    private string formattedText = "";
    private bool busy, syncingSelection, micaEnabled;

    public TextToolWindow(CaptureTools tools, ShotDocument? image, bool translation)
    {
        this.tools = tools;
        Title = image is null ? "YXRL 粘贴文字翻译" : translation ? "YXRL 截图翻译" : "YXRL 提取文字";
        Width = image is null ? 540 : 880;
        Height = 580;
        MinWidth = 380;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetAutomationId(this, "YxrlCaptureText");

        var root = new DockPanel { Margin = new Thickness(14, 12, 14, 12) };
        Content = root;

        // Top layout & window behavior toolbar
        var barWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (image is not null)
        {
            foreach (var item in new[] { "智能分段", "合并换行", "保留换行" }) paragraphMode.Items.Add(item);
            paragraphMode.SelectedIndex = 0;
            AutomationProperties.SetName(paragraphMode, "段落模式");
            foreach (var item in new[] { "原标点", "中文标点", "英文标点" }) punctuationMode.Items.Add(item);
            punctuationMode.SelectedIndex = 0;
            AutomationProperties.SetName(punctuationMode, "标点转换");
            barWrap.Children.Add(MutedLabel("排版"));
            barWrap.Children.Add(paragraphMode);
            barWrap.Children.Add(removeSpaces);
            barWrap.Children.Add(punctuationMode);
            barWrap.Children.Add(MutedLabel("字号"));
            foreach (var (lbl, sz) in new[] { ("小", 12.0), ("中", 14.0), ("大", 16.5) })
            {
                var sizeBtn = ActionButton(lbl, () => { source.FontSize = result.FontSize = sz; });
                sizeBtn.Padding = new Thickness(8, 2, 8, 2);
                sizeBtn.MinHeight = 26;
                barWrap.Children.Add(sizeBtn);
            }
            barWrap.Children.Add(showOriginal);
            barWrap.Children.Add(closeAfterCopy);
        }
        barWrap.Children.Add(pinTopmost);
        topBar = CardBorder(barWrap);
        topBar.Margin = new Thickness(0, 0, 0, 10);
        topBar.Padding = new Thickness(10, 7, 10, 7);
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(status, Dock.Bottom);
        status.Margin = new Thickness(2, 8, 2, 0);
        root.Children.Add(status);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        columns.ColumnDefinitions.Add(new());
        columns.ColumnDefinitions.Add(new());
        columns.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var leftStack = new DockPanel();
        var leftTitle = Label("原图（划选或双击文字块可同步右侧选区）");
        DockPanel.SetDock(leftTitle, Dock.Top);
        leftStack.Children.Add(leftTitle);
        leftStack.Children.Add(imageCanvas);
        leftCard = CardBorder(leftStack);

        var rightStack = new StackPanel();
        rightStack.Children.Add(Label("识别与校对原文"));
        rightStack.Children.Add(source);
        var srcBar = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        srcBar.Children.Add(ActionButton("复制原文", () => Copy(source, allowCloseAll: true)));
        recognize = ActionButton("重新提取", () => _ = Recognize());
        srcBar.Children.Add(recognize);
        rightStack.Children.Add(srcBar);

        var trBar = new WrapPanel { Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        trBar.Children.Add(MutedLabel("目标语言"));
        foreach (var preset in new[] { "简体中文", "繁體中文", "英文", "日文", "韩文", "法文", "德文" })
            language.Items.Add(preset);
        language.Text = tools.Translation.TargetLanguage;
        AutomationProperties.SetName(language, "目标语言");
        trBar.Children.Add(language);
        translate = ActionButton("翻译", () => _ = Translate(), primary: true);
        trBar.Children.Add(translate);
        rightStack.Children.Add(trBar);

        rightStack.Children.Add(Label("译文"));
        result.IsReadOnly = true;
        rightStack.Children.Add(result);
        var footer = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        footer.Children.Add(ActionButton("复制译文", () => Copy(result, allowCloseAll: false)));
        footer.Children.Add(ActionButton("翻译设置", tools.OpenSettings));
        rightStack.Children.Add(footer);
        rightCard = CardBorder(new ScrollViewer { Content = rightStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        columns.Children.Add(leftCard);
        columns.Children.Add(rightCard);
        root.Children.Add(columns);

        if (image is null)
        {
            leftCard.Visibility = Visibility.Collapsed;
            recognize.Visibility = Visibility.Collapsed;
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
        source.TextChanged += (_, _) => result.Clear();
        source.SelectionChanged += SyncTextSelectionToImage;
        imageCanvas.LinesSelected += SyncImageSelectionToText;

        SizeChanged += (_, _) => LayoutColumns();
        Loaded += async (_, _) =>
        {
            LayoutColumns();
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
        Grid.SetRow(rightCard, split ? 0 : 1);
        columns.ColumnDefinitions[0].Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        columns.ColumnDefinitions[1].Width = split ? new GridLength(1.15, GridUnitType.Star) : new GridLength(0);
        columns.RowDefinitions[0].Height = split ? new GridLength(1, GridUnitType.Star) : showLeft ? new GridLength(0.75, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        columns.RowDefinitions[1].Height = split ? new GridLength(0) : showLeft ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        rightCard.Margin = split ? new Thickness(10, 0, 0, 0) : showLeft ? new Thickness(0, 10, 0, 0) : new Thickness(0);
    }

    private static Border CardBorder(UIElement child)
    {
        var border = new Border
        {
            Child = child,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10)
        };
        border.SetResourceReference(Border.BackgroundProperty, "Paper");
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }

    private static TextBlock Label(string name) => new()
    {
        Text = name,
        FontWeight = FontWeights.SemiBold,
        FontSize = 13.5,
        Margin = new Thickness(0, 0, 0, 6)
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
            MinHeight = 96,
            MaxHeight = 220,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(field, name);
        return field;
    }

    private Button ActionButton(string label, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        if (TryFindResource(primary ? "PrimaryButton" : "SecondaryButton") is Style st)
            button.Style = st;
        button.Click += (_, _) => action();
        return button;
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
    }

    private void SyncTextSelectionToImage(object sender, RoutedEventArgs e)
    {
        if (syncingSelection || currentSpans.Count == 0 || source.Text != formattedText)
            return;
        int start = source.SelectionStart, length = source.SelectionLength;
        if (length <= 0)
        {
            imageCanvas.SetHighlights([]);
            return;
        }
        int end = start + length;
        var hits = new List<int>();
        foreach (var span in currentSpans)
            if (span.Length > 0 && span.Start < end && span.Start + span.Length > start)
                hits.Add(span.LineIndex);
        imageCanvas.SetHighlights(hits);
    }

    private void SyncImageSelectionToText(IReadOnlyList<int> lineIndices)
    {
        if (currentSpans.Count == 0 || source.Text != formattedText)
            return;
        var matching = currentSpans.Where(s => lineIndices.Contains(s.LineIndex) && s.Length > 0).ToArray();
        if (matching.Length == 0)
            return;
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
    }

    private void Copy(TextBox editor, bool allowCloseAll)
    {
        bool all = editor.SelectionLength == 0;
        var text = all ? editor.Text : editor.SelectedText;
        if (string.IsNullOrEmpty(text))
        {
            status.Text = "没有可复制的文字。";
            return;
        }

        try
        {
            Clipboard.SetText(text);
            status.Text = "已复制文字。";
            if (allowCloseAll && all && closeAfterCopy.IsChecked == true)
                Close();
        }
        catch (COMException)
        {
            status.Text = "剪贴板正在使用，请重试。";
        }
    }

    private void SetBusy(bool value, string message)
    {
        busy = value;
        translate.IsEnabled = recognize.IsEnabled = !value;
        source.IsReadOnly = value;
        status.Text = message;
    }

    private async Task Recognize()
    {
        if (busy || ocrInput is null)
            return;
        var token = life.Token;
        SetBusy(true, "正在本机识别文字…");
        try
        {
            var doc = await tools.Ocr.ReadDocumentAsync(ocrInput, token);
            if (token.IsCancellationRequested)
                return;
            document = doc;
            imageCanvas.SetContent(displayInput, doc.Lines);
            var formatted = OcrLayout.Format(doc, CurrentLayoutOptions());
            currentSpans = formatted.Spans;
            formattedText = formatted.Text;
            syncingSelection = true;
            try { source.Text = formatted.Text; }
            finally { syncingSelection = false; }
            result.Clear();
            SetBusy(false, doc.Lines.Count == 0 ? "没有识别到文字，可重新框选或手动输入。" : $"识别完成（{doc.Lines.Count} 段），可在左图划选或在右侧校对。");
            source.Focus();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or TypeInitializationException or DllNotFoundException or ArgumentException)
        {
            if (!token.IsCancellationRequested)
                SetBusy(false, "本机 OCR 失败，可重试。" + ex.GetBaseException().Message);
        }
    }

    private async Task Translate()
    {
        if (busy)
            return;
        var text = source.SelectionLength > 0 ? source.SelectedText : source.Text;
        var token = life.Token;
        result.Clear();
        SetBusy(true, "正在翻译…");
        try
        {
            var options = tools.Translation;
            var endpoint = options.Endpoint() ?? throw new FormatException("请先打开「翻译设置」，填写 API 基础地址和模型名。");
            var translated = await tools.Translator.Translate(options, text, language.Text, TranslationCredential.Read(endpoint), token);
            if (!token.IsCancellationRequested)
            {
                result.Text = translated;
                SetBusy(false, "翻译完成。");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or OperationCanceledException or IOException or JsonException or Win32Exception or InvalidDataException)
        {
            if (!token.IsCancellationRequested)
                SetBusy(false, ex is OperationCanceledException ? "翻译超时，请重试。" : ex is JsonException ? "翻译服务返回了无法识别的响应。" : ex.Message);
        }
    }
}
