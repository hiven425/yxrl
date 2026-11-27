using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed record ClosedPin(ShotDocument Document, PixelRect Placement, double Opacity);

internal sealed class CaptureTools : IDisposable
{
    private readonly SettingsStore settings;
    private readonly HashSet<Window> windows = [];
    private readonly List<PixelRect> selectionHistory = [];
    private readonly List<ClosedPin> closedPins = [];
    private ShotWindow? selection;
    private RecordingWindow? recorder;
    private ScrollWindow? scrolling;
    private RecordingRecoveryWindow? recovery;
    private bool disposing;
    private string RecordingsFolder => Path.Combine(settings.DirectoryPath, "RecordingRecovery");
    public ShotHotkeys Hotkeys { get; }
    public OcrReader Ocr { get; }
    public TranslationService Translator { get; } = new();
    public CaptureOptions Options => settings.Current.Capture;
    public TranslationOptions Translation => settings.Current.Translation;
    public Action OpenSettings { get; }
    public int SelectionHistoryCount => selectionHistory.Count;
    public bool HasClosedPins => closedPins.Count > 0;

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
                case ShotAction.Record:
                    if (selection is not null)
                    {
                        selection.Activate();
                        return;
                    }

                    var frame = DesktopFrame.Freeze();
                    selection = new(this, new(frame.Image), frame, recordingSelection: action == ShotAction.Record);
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
                case ShotAction.RestorePin:
                    if (!RestoreLastPin())
                        Feedback("没有已关闭的贴图可恢复。");
                    break;
            }
        }
        catch (Exception ex) when (ex is ExternalException or Win32Exception or InvalidOperationException or IOException or NotSupportedException)
        {
            Feedback("打开截图工具失败。" + ex.Message);
        }
    }

    public void RecordSelection(PixelRect area)
    {
        if (!area.HasArea)
            return;
        if (selectionHistory.Count > 0 && selectionHistory[^1] == area)
            return;
        selectionHistory.Add(area);
        if (selectionHistory.Count > 20)
            selectionHistory.RemoveAt(0);
    }

    public PixelRect? GetSelectionHistory(int reverseIndex)
    {
        if (selectionHistory.Count == 0 || reverseIndex < 0 || reverseIndex >= selectionHistory.Count)
            return null;
        return selectionHistory[selectionHistory.Count - 1 - reverseIndex];
    }

    public void RememberClosedPin(ShotDocument document, PixelRect placement, double opacity)
    {
        if (disposing || !placement.HasArea)
            return;
        closedPins.Add(new(document, placement, opacity));
        if (closedPins.Count > 10)
            closedPins.RemoveAt(0);
    }

    public bool RestoreLastPin()
    {
        if (closedPins.Count == 0)
            return false;
        var last = closedPins[^1];
        closedPins.RemoveAt(closedPins.Count - 1);
        Show(new ShotWindow(this, last.Document, placement: last.Placement, initialOpacity: last.Opacity));
        return true;
    }

    public void RefreshTheme()
    {
        foreach (var win in windows.ToArray())
        {
            if (win is TextToolWindow text)
                text.RefreshTheme();
            else if (win is ShotWindow shot)
                shot.RefreshTheme();
        }
    }

    internal void OpenRecorder(ShotWindow owner, PixelRect area)
    {
        var even = area with { Right = area.Right - area.Width % 2, Bottom = area.Bottom - area.Height % 2 };
        if (even.Width < Mp4Writer.MinimumEdge || even.Height < Mp4Writer.MinimumEdge)
        {
            Feedback($"录制区域至少需要 {Mp4Writer.MinimumEdge} × {Mp4Writer.MinimumEdge} 像素。请扩大选区。");
            return;
        }
        owner.Close();
        OpenRecording(even);
    }

    private void OpenRecording(PixelRect area)
    {
        if (!Options.Enabled)
        {
            Feedback("截图工具已关闭，可在「截图与翻译」设置中开启。");
            return;
        }
        if (recorder is not null)
        {
            recorder.Activate();
            return;
        }
        var window = new RecordingWindow(area, RecordingsFolder, clip => ShowRecording(clip, area));
        recorder = window;
        window.Closed += (_, _) => recorder = null;
        Show(window);
    }

    private void ShowRecording(RecordedClip clip, PixelRect area) => Show(new RecordingPreview(clip, () => OpenRecording(area)));

    public void OfferRecovery(bool interactive = false)
    {
        if (recovery is not null)
        {
            if (interactive) recovery.Activate();
            return;
        }
        try
        {
            var claimed = windows.OfType<RecordingPreview>().Select(window => window.SourceClip.Session?.Folder).ToHashSet();
            var sessions = RecordingSession.Recoverable(RecordingsFolder)
                .Where(session => session.Folder != recorder?.ActiveSession?.Folder && !claimed.Contains(session.Folder))
                .OrderByDescending(session => session.Header.Started).ToArray();
            if (sessions.Length == 0)
            {
                if (interactive) Feedback("没有可恢复的录屏。");
                return;
            }
            recovery = new RecordingRecoveryWindow(sessions, clip => ShowRecording(clip, clip.Session!.Header.Area));
            recovery.Closed += (_, _) => recovery = null;
            Show(recovery);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (interactive) Feedback("无法读取录屏恢复目录。 " + ex.Message);
        }
    }

    internal void OpenScroll(ShotWindow owner, PixelRect area)
    {
        if (area.Width < 32 || area.Height < 32)
        {
            Feedback("长截图区域至少需要 32 × 32 像素。请扩大选区。");
            return;
        }
        owner.Close();
        if (scrolling is not null)
        {
            scrolling.Activate();
            return;
        }
        scrolling = new ScrollWindow(area, image => Show(new ShotWindow(this, new ShotDocument(image))));
        scrolling.Closed += (_, _) => scrolling = null;
        Show(scrolling);
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

            if (owner is ShotWindow { Selecting: true } && place is { } area)
                RecordSelection(area);

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
        disposing = true;
        Hotkeys.Dispose();
        PreserveRecordings();
        foreach (var win in windows.ToArray()) win.Close();
        Ocr.Dispose();
        Translator.Dispose();
    }

    public void PreserveRecordings()
    {
        recorder?.Shutdown();
        foreach (var preview in windows.OfType<RecordingPreview>().ToArray()) preview.Shutdown();
    }
}
