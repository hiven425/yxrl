using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed class CaptureTools : IDisposable
{
    private readonly SettingsStore settings;
    private readonly HashSet<Window> windows = [];
    private ShotWindow? selection;
    public ShotHotkeys Hotkeys { get; }
    public OcrReader Ocr { get; }
    public TranslationService Translator { get; } = new();
    public CaptureOptions Options => settings.Current.Capture;
    public TranslationOptions Translation => settings.Current.Translation;
    public Action OpenSettings { get; }

    internal CaptureTools(SettingsStore settings, Action openSettings)
    {
        this.settings = settings;
        OpenSettings = openSettings;
        Ocr = new(settings.DirectoryPath);
        Hotkeys = new(Invoke);
        Hotkeys.Apply(Options, false);
    }

    private void Show(Window window)
    {
        windows.Add(window);
        window.Closed += (_, _) =>
        {
            windows.Remove(window);
            if (selection == window)
                selection = null;
        };
        window.Show();
    }

    private static void Feedback(string message) => MessageBox.Show(message, "YXRL 截图工具", MessageBoxButton.OK, MessageBoxImage.Information);
    public void Invoke(ShotAction action)
    {
        if (!Options.Enabled)
        {
            Feedback("截图工具已关闭，可在「截图与翻译」设置中开启。");
            return;
        }

        try
        {
            switch (action)
            {
                case ShotAction.Start:
                    if (selection is not null)
                    {
                        selection.Activate();
                        return;
                    }

                    var frame = DesktopFrame.Freeze();
                    selection = new(this, new(frame.Image), frame);
                    Show(selection);
                    break;
                case ShotAction.Clipboard:
                    if (ReadClipboardImage(Clipboard.GetDataObject()) is not { } image)
                    {
                        Feedback("剪贴板中没有图片。");
                        return;
                    }

                    Show(new ShotWindow(this, new(image)));
                    break;
                case ShotAction.Translate:
                    Show(new TextToolWindow(this, null, true));
                    break;
            }
        }
        catch (Exception ex) when (ex is ExternalException or Win32Exception or InvalidOperationException or IOException or NotSupportedException)
        {
            Feedback("打开截图工具失败。" + ex.Message);
        }
    }

    internal static BitmapSource? ReadClipboardImage(IDataObject? data)
    {
        if (data is null)
            return null;

        BitmapSource? ReadBitmap() =>
            data.TryGetData(DataFormats.Bitmap, autoConvert: true, out BitmapSource? bitmap) ? bitmap : null;

        // Windows can synthesize a bitmap with all alpha bytes zero from an opaque 24-bit DIB.
        if (!data.TryGetData("PNG", autoConvert: false, out MemoryStream? png))
            return ReadBitmap();

        long position = png.Position;
        try
        {
            png.Position = 0;
            return new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or IOException)
        {
            return ReadBitmap() ?? throw new IOException("无法解码剪贴板中的 PNG 图片。", ex);
        }
        finally
        {
            png.Position = position;
        }
    }

    public bool Output(Window owner, ImageAction action, ShotDocument image, PixelRect? place)
    {
        try
        {
            if (!Options.Enabled && action is not (ImageAction.Copy or ImageAction.Save))
            {
                Feedback("截图工具已关闭。");
                return false;
            }

            switch (action)
            {
                case ImageAction.Copy:
                    Clipboard.SetImage(image.Render());
                    break;
                case ImageAction.Save:
                    var dialog = new SaveFileDialog
                    {
                        Filter = "PNG 图片 (*.png)|*.png",
                        DefaultExt = ".png",
                        AddExtension = true,
                        FileName = $"YXRL-{DateTime.Now:yyyyMMdd-HHmmss}.png"};
                    if (dialog.ShowDialog(owner) != true)
                        return false;
                    SavePng(image, dialog.FileName);
                    break;
                case ImageAction.Pin:
                    Show(new ShotWindow(this, image, placement: place));
                    break;
                case ImageAction.Ocr:
                case ImageAction.Translate:
                    Show(new TextToolWindow(this, image, action == ImageAction.Translate));
                    break;
            }

            return true;
        }
        catch (Exception ex) when (ex is ExternalException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            Feedback("截图操作失败。" + ex.Message);
            return false;
        }
    }

    internal static void SavePng(ShotDocument image, string path)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image.RenderForExport()));
            using (var stream = File.Create(tmp))
                png.Save(stream);
            File.Move(tmp, path, true);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    public void Save(Preferences next, string? editedSecret)
    {
        next.Capture.Validate();
        next.Translation.Validate();
        var old = Options;
        bool rebind = next.Capture != old;
        Uri? endpoint = null;
        string? previous = null;
        if (editedSecret is not null)
        {
            endpoint = next.Translation.Endpoint() ?? throw new FormatException("保存密钥前请填写 API 地址和模型名。");
            previous = TranslationCredential.Read(endpoint);
        }

        if (rebind)
            Hotkeys.Apply(next.Capture, true);
        bool wrote = false;
        try
        {
            if (endpoint is not null)
            {
                TranslationCredential.Write(endpoint, editedSecret);
                wrote = true;
            }

            settings.Save(next);
        }
        catch
        {
            try
            {
                if (wrote)
                    TranslationCredential.Write(endpoint!, previous);
            }
            finally
            {
                if (rebind)
                    Hotkeys.Apply(old, false);
            }

            throw;
        }

        if (!next.Capture.Enabled)
            selection?.Close();
    }

    public void Dispose()
    {
        Hotkeys.Dispose();
        foreach (var win in windows.ToArray())
            win.Close();
        Ocr.Dispose();
        Translator.Dispose();
    }
}
