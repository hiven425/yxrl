using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Win32;
using Yxrl.Views;

namespace Yxrl.Capture;

internal sealed class RecognitionWindow : Window
{
    private readonly RecognitionModels models;
    private readonly OcrReader ocr;
    private readonly BitmapSource image;
    private readonly RecognitionKind kind;
    private readonly ContentControl result = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar progress = new() { Height = 4, Visibility = Visibility.Collapsed, Maximum = 100 };
    private readonly Button action, copy, export;
    private CancellationTokenSource? operation;
    private Func<string>? text;
    private Action<string>? save;

    public RecognitionWindow(RecognitionModels models, OcrReader ocr, ShotDocument screenshot, RecognitionKind kind)
    {
        this.models = models;
        this.ocr = ocr;
        this.kind = kind;
        image = screenshot.Render(forOcr: true);
        string label = kind switch
        {
            RecognitionKind.Document => "文档识别",
            RecognitionKind.Table => "图片转表格",
            _ => "公式识别"
        };
        Title = "YXRL " + label;
        if (Theme.AppIcon is { } icon) Icon = icon;
        Width = 900;
        Height = 560;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Surface");

        var root = new DockPanel { Margin = new Thickness(14) };
        Content = root;
        var commands = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        DockPanel.SetDock(commands, Dock.Bottom);
        root.Children.Add(commands);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Right);
        commands.Children.Add(buttons);
        action = Command("下载模型", () =>
        {
            if (operation is { } pending)
                pending.Cancel();
            else
                _ = Recognize(download: true);
        }, primary: true);
        copy = Command(kind == RecognitionKind.Formula ? "复制 LaTeX" : "复制", Copy);
        export = Command(kind == RecognitionKind.Table ? "导出 Excel" : "导出 Word", Export, primary: true);
        copy.Visibility = export.Visibility = Visibility.Collapsed;
        buttons.Children.Add(action);
        buttons.Children.Add(copy);
        buttons.Children.Add(export);
        status.Text = $"首次使用需下载本机模型（{models.DownloadSize(kind) / 1e6:F1} MB）。安装后可离线识别。";
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        commands.Children.Add(status);
        DockPanel.SetDock(progress, Dock.Bottom);
        root.Children.Add(progress);

        var panes = new Grid();
        root.Children.Add(panes);
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star), MinWidth = 160 });
        panes.Children.Add(Pane("原图", new Image { Source = image, Stretch = Stretch.Uniform }, 0));
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
        splitter.SetResourceReference(BackgroundProperty, "PanelBorder");
        Grid.SetColumn(splitter, 1);
        panes.Children.Add(splitter);
        panes.Children.Add(Pane("识别结果", result, 2));
        Loaded += (_, _) =>
        {
            if (models.IsInstalled(kind))
                _ = Recognize(download: false);
        };
        Closed += (_, _) => operation?.Cancel();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    private async Task Recognize(bool download)
    {
        using var pending = new CancellationTokenSource();
        operation = pending;
        var token = pending.Token;
        action.Content = "取消";
        status.Text = "正在校验本机模型…";
        progress.Visibility = Visibility.Visible;
        progress.IsIndeterminate = true;
        try
        {
            bool receiving = true;
            var updates = new Progress<ModelDownload>(data =>
            {
                if (!receiving || token.IsCancellationRequested)
                    return;
                int percent = (int)(data.Received * 100 / data.Total);
                progress.IsIndeterminate = false;
                progress.Value = percent;
                status.Text = $"模型下载 {percent}%";
            });
            var files = await models.Prepare(kind, download, updates, token);
            receiving = false;
            status.Text = "正在本机识别…";
            progress.IsIndeterminate = true;
            if (kind == RecognitionKind.Formula)
            {
                var latex = await Task.Run(() => StructureRecognition.ReadFormula(files, image, token), token);
                token.ThrowIfCancellationRequested();
                ShowFormula(latex);
            }
            else
            {
                var recognized = await ocr.ReadDocumentAsync(image, token);
                if (kind == RecognitionKind.Document)
                {
                    var blocks = await Task.Run(() => StructureRecognition.ReadDocument(files[0], image, recognized, token), token);
                    token.ThrowIfCancellationRequested();
                    if (blocks.Length == 0)
                        throw new InvalidDataException("选区没有识别出可导出的内容。");
                    ShowDocument(blocks);
                }
                else
                {
                    var table = await Task.Run(() => StructureRecognition.ReadTable(files[0], image, recognized, token), token);
                    token.ThrowIfCancellationRequested();
                    ShowTable(table);
                }
            }
            status.Text = "可对照原图校对识别结果。";
            action.Visibility = Visibility.Collapsed;
            copy.Visibility = Visibility.Visible;
            export.Visibility = save is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException ex)
        {
            status.Text = token.IsCancellationRequested ? "操作已取消。" : "下载超时，请检查网络后重试。";
            if (!token.IsCancellationRequested)
                System.Diagnostics.Trace.TraceWarning("Recognition download timed out: {0}", ex.GetType().Name);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or OnnxRuntimeException)
        {
            status.Text = ex switch
            {
                HttpRequestException => "模型下载失败，请检查网络后重试。",
                OnnxRuntimeException => "本机模型运行失败，请重新打开窗口后重试。",
                UnauthorizedAccessException => "无法访问本机模型目录。",
                _ => ex.Message
            };
        }
        finally
        {
            operation = null;
            action.Content = "重试";
            progress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowFormula(string latex)
    {
        var editor = TextEditor(latex, "LaTeX 源码");
        editor.FontFamily = new FontFamily("Consolas");
        result.Content = editor;
        text = () => editor.Text;
    }

    private void ShowDocument(DocumentBlock[] blocks)
    {
        var stack = new StackPanel();
        var fields = new TextBox?[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            if (block.Image is { } figure)
            {
                stack.Children.Add(new Image { Source = figure, MaxHeight = 200, Margin = new Thickness(0, 0, 0, 6), Stretch = Stretch.Uniform });
                continue;
            }
            var field = TextEditor(block.Text, block.Heading ? "标题" : "段落");
            if (block.Heading) field.FontWeight = FontWeights.SemiBold;
            field.Margin = new Thickness(0, 0, 0, 6);
            fields[i] = field;
            stack.Children.Add(field);
        }
        result.Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        text = () => string.Join(Environment.NewLine + Environment.NewLine, fields.OfType<TextBox>().Select(field => field.Text));
        save = path => RecognitionExport.SaveDocument(path, blocks.Select((block, i) =>
            fields[i] is { } field ? block with { Text = field.Text } : block).ToArray());
    }

    private void ShowTable(RecognizedTable table)
    {
        var grid = new Grid();
        for (int row = 0; row < table.Rows; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 30 });
        for (int column = 0; column < table.Columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 80 });
        var fields = table.Cells.Select(cell =>
        {
            var editor = TextEditor(cell.Text, $"第 {cell.Row + 1} 行第 {cell.Column + 1} 列");
            editor.MinHeight = 28;
            editor.MaxWidth = 220;
            Grid.SetRow(editor, cell.Row);
            Grid.SetColumn(editor, cell.Column);
            Grid.SetRowSpan(editor, cell.Rows);
            Grid.SetColumnSpan(editor, cell.Columns);
            grid.Children.Add(editor);
            return editor;
        }).ToArray();
        result.Content = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        RecognizedTable Edited() => table with { Cells = table.Cells.Select((cell, i) => cell with { Text = fields[i].Text }).ToArray() };
        text = () => Edited().ToTsv();
        save = path => RecognitionExport.SaveTable(path, Edited());
    }

    private void Copy()
    {
        if (text?.Invoke() is not { Length: > 0 } value)
        {
            status.Text = "没有可复制的文字。";
            return;
        }
        try { Clipboard.SetText(value); status.Text = "已复制。"; }
        catch (ExternalException) { status.Text = "剪贴板正忙，请重试。"; }
    }

    private void Export()
    {
        if (save is null)
            return;
        bool table = kind == RecognitionKind.Table;
        string extension = table ? ".xlsx" : ".docx";
        var dialog = new SaveFileDialog
        {
            Filter = table ? "Excel 工作簿 (*.xlsx)|*.xlsx" : "Word 文档 (*.docx)|*.docx",
            AddExtension = true, DefaultExt = extension,
            FileName = $"YXRL-识别结果-{DateTime.Now:yyyyMMdd-HHmmss}{extension}"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try { save(dialog.FileName); status.Text = "已导出。"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = "导出失败。" + ex.Message;
        }
    }

    private static Border Pane(string title, UIElement content, int column)
    {
        var dock = new DockPanel();
        var label = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(label, Dock.Top);
        dock.Children.Add(label);
        dock.Children.Add(content);
        var border = new Border { Child = dock, Padding = new Thickness(10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        border.SetResourceReference(BorderBrushProperty, "PanelBorder");
        border.SetResourceReference(Border.BackgroundProperty, "Card");
        Grid.SetColumn(border, column);
        return border;
    }

    private static TextBox TextEditor(string value, string name)
    {
        var field = new TextBox { Text = value, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Padding = new Thickness(6) };
        AutomationProperties.SetName(field, name);
        return field;
    }

    private static Button Command(string label, Action click, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(5, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, primary ? "PrimaryButton" : "SecondaryButton");
        button.Click += (_, _) => click();
        return button;
    }
}
