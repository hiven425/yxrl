using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RapidOcrNet;
using SkiaSharp;

namespace Yxrl.Capture;

internal readonly record struct OcrLine(string Text, Rect Bounds, Point[] Polygon, double Score);
internal sealed record OcrDocument(IReadOnlyList<OcrLine> Lines, int ImageWidth, int ImageHeight);

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

    public Task<OcrDocument> ReadDocumentAsync(BitmapSource image, CancellationToken token) => Task.Run(() =>
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
            var raw = new List<OcrLine>();
            foreach (var block in result.TextBlocks)
            {
                string text = block.Text?.Trim() ?? "";
                if (text.Length == 0)
                    continue;
                var poly = block.BoxPoints?.Select(pt => new Point(pt.X, pt.Y)).ToArray() ?? [];
                Rect box = poly.Length > 0
                    ? new Rect(poly.Min(p => p.X), poly.Min(p => p.Y),
                        Math.Max(1, poly.Max(p => p.X) - poly.Min(p => p.X)),
                        Math.Max(1, poly.Max(p => p.Y) - poly.Min(p => p.Y)))
                    : Rect.Empty;
                double score = block.CharScores is { Length: > 0 } ? block.CharScores.Average() : 1.0;
                raw.Add(new OcrLine(text, box, poly, score));
            }
            var sorted = SortReadingOrder(raw);
            return new OcrDocument(sorted, pixels.PixelWidth, pixels.PixelHeight);
        }
    }, token);

    private static OcrLine[] SortReadingOrder(List<OcrLine> lines)
    {
        if (lines.Count <= 1)
            return [.. lines];
        var ordered = lines.OrderBy(l => l.Bounds.IsEmpty ? 0 : l.Bounds.Top).ThenBy(l => l.Bounds.IsEmpty ? 0 : l.Bounds.Left).ToList();
        var rows = new List<List<OcrLine>>();
        foreach (var line in ordered)
        {
            if (rows.Count > 0 && SameVisualRow(rows[^1][^1].Bounds, line.Bounds))
                rows[^1].Add(line);
            else
                rows.Add([line]);
        }
        var flat = new List<OcrLine>(lines.Count);
        foreach (var row in rows)
        {
            row.Sort((a, b) => a.Bounds.Left.CompareTo(b.Bounds.Left));
            flat.AddRange(row);
        }
        return [.. flat];
    }

    private static bool SameVisualRow(Rect a, Rect b)
    {
        if (a.IsEmpty || b.IsEmpty)
            return false;
        double overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return overlap > 0.45 * Math.Min(a.Height, b.Height);
    }

    public async Task<string> ReadAsync(BitmapSource image, CancellationToken token)
    {
        var doc = await ReadDocumentAsync(image, token).ConfigureAwait(false);
        return string.Join(Environment.NewLine, doc.Lines.Select(line => line.Text));
    }
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
