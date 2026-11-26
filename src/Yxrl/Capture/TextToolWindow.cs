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

namespace Yxrl.Capture;

internal sealed class TextToolWindow : Window
{
    private readonly TextBox source = Editor("原文"), result = Editor("译文");
    private readonly ComboBox language = new()
    {
        IsEditable = true,
        MinWidth = 105,
        Margin = new Thickness(4)
    };
    private readonly TextBlock status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(5)
    };
    private readonly Image original = new()
    {
        Stretch = Stretch.Uniform,
        Margin = new Thickness(4)
    };
    private readonly Grid columns = new();
    private readonly StackPanel left = new(), right = new();
    private readonly Button translate, recognize;
    private readonly BitmapSource? ocrInput;
    private readonly CaptureTools tools;
    private readonly CancellationTokenSource life = new();
    private bool busy;
    public TextToolWindow(CaptureTools tools, ShotDocument? image, bool translation)
    {
        this.tools = tools;
        Title = image is null ? "YXRL 粘贴文字翻译" : translation ? "YXRL 截图翻译" : "YXRL 提取文字";
        Width = image is null ? 510 : 760;
        Height = 520;
        MinWidth = 340;
        MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetAutomationId(this, "YxrlCaptureText");
        var root = new DockPanel
        {
            Margin = new Thickness(14)
        };
        Content = root;
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        root.Children.Add(new ScrollViewer { Content = columns, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        columns.ColumnDefinitions.Add(new());
        columns.ColumnDefinitions.Add(new());
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        columns.Children.Add(left);
        columns.Children.Add(right);
        left.Children.Add(Label("原图"));
        left.Children.Add(original);
        right.Children.Add(Label("原文"));
        right.Children.Add(source);
        right.Children.Add(ActionButton("复制原文", () => Copy(source)));
        var bar = new WrapPanel
        {
            Margin = new Thickness(0, 6, 0, 2)
        };
        right.Children.Add(bar);
        bar.Children.Add(Label("目标语言"));
        bar.Children.Add(language);
        language.Items.Add("简体中文");
        language.Items.Add("英文");
        language.Text = tools.Translation.TargetLanguage;
        AutomationProperties.SetName(language, "目标语言");
        translate = ActionButton("翻译", () => _ = Translate());
        bar.Children.Add(translate);
        recognize = ActionButton("重新提取", () => _ = Recognize());
        bar.Children.Add(recognize);
        right.Children.Add(Label("译文"));
        result.IsReadOnly = true;
        right.Children.Add(result);
        source.TextChanged += (_, _) => result.Clear();
        var footer = new WrapPanel();
        right.Children.Add(footer);
        footer.Children.Add(ActionButton("复制译文", () => Copy(result)));
        footer.Children.Add(ActionButton("翻译设置", tools.OpenSettings));
        if (image is null)
        {
            left.Visibility = Visibility.Collapsed;
            recognize.Visibility = Visibility.Collapsed;
        }
        else
        {
            original.Source = image.Render();
            ocrInput = image.Render(forOcr: true);
        }

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

    private void LayoutColumns()
    {
        bool split = ocrInput is not null && ActualWidth >= 620;
        Grid.SetColumn(right, split ? 1 : 0);
        Grid.SetRow(right, split ? 0 : 1);
        columns.ColumnDefinitions[1].Width = split ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
        original.MaxHeight = split ? 400 : 140;
        right.Margin = new Thickness(split ? 12 : 0, 0, 0, 0);
    }

    private static TextBlock Label(string name) => new()
    {
        Text = name,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(3, 6, 3, 4)
    };
    private static TextBox Editor(string name)
    {
        var field = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 82,
            MaxHeight = 210,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(field, name);
        return field;
    }

    private static Button ActionButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void Copy(TextBox editor)
    {
        var text = editor.SelectionLength > 0 ? editor.SelectedText : editor.Text;
        if (string.IsNullOrEmpty(text))
        {
            status.Text = "没有可复制的文字。";
            return;
        }

        try
        {
            Clipboard.SetText(text);
            status.Text = "已复制文字。";
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
            var text = await tools.Ocr.ReadAsync(ocrInput, token);
            if (token.IsCancellationRequested)
                return;
            source.Text = text;
            result.Clear();
            SetBusy(false, text.Length == 0 ? "没有识别到文字，可重新框选或手动输入。" : "识别完成，可校对或选择原文。");
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
