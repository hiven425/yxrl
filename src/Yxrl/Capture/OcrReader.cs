using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RapidOcrNet;
using SkiaSharp;

namespace Yxrl.Capture;

internal sealed class OcrReader(string dataDirectory) : IDisposable
{
    private readonly object gate = new();
    private RapidOcr? engine;
    private bool disposed;
    private RapidOcr Load()
    {
        var directory = Path.Combine(dataDirectory, "ocr", "ppocrv5-4.2.0-1");
        Directory.CreateDirectory(directory);
        string Asset(string name)
        {
            using var input = typeof(OcrReader).Assembly.GetManifestResourceStream("Ocr." + name) ?? throw new InvalidDataException("发布包缺少 OCR 资源 " + name);
            var dest = Path.Combine(directory, name);
            if (File.Exists(dest) && new FileInfo(dest).Length == input.Length)
                return dest;
            string temporary = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = File.Create(temporary))
                    input.CopyTo(file);
                File.Move(temporary, dest, true);
            }
            finally
            {
                File.Delete(temporary);
            }

            return dest;
        }

        var ocr = new RapidOcr();
        try
        {
            ocr.InitModels(Asset("ch_PP-OCRv5_mobile_det.onnx"), Asset("ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"), Asset("ch_PP-OCRv5_rec_mobile.onnx"), Asset("ppocrv5_dict.txt"), Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
            return ocr;
        }
        catch
        {
            ocr.Dispose();
            throw;
        }
    }

    public Task<string> ReadAsync(BitmapSource image, CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            token.ThrowIfCancellationRequested();
            engine ??= Load();
            var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            using var bitmap = new SKBitmap(pixels.PixelWidth, pixels.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            pixels.CopyPixels(System.Windows.Int32Rect.Empty, bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
            var result = engine.Detect(bitmap, RapidOcrOptions.Default, token);
            return string.Join(Environment.NewLine, result.TextBlocks.Select(block => block.Text.Trim()).Where(line => line.Length > 0));
        }
    }, token);
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            engine?.Dispose();
            engine = null;
        }
    }
}
