using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yxrl.Capture;

internal enum StitchResult { Added, Unchanged, NoMatch, Limit }

internal sealed class ScrollStitch(bool horizontal)
{
    private const int MaximumAxis = 20_000, MaximumPixels = 24_000_000;
    private readonly List<BitmapSource> strips = [];
    private uint[]? previous;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public BitmapSource? LastStrip => strips.Count > 0 ? strips[^1] : null;
    public bool Horizontal { get; } = horizontal;

    public StitchResult Append(BitmapSource frame)
    {
        int length = Horizontal ? frame.PixelWidth : frame.PixelHeight;
        int cross = Horizontal ? frame.PixelHeight : frame.PixelWidth;
        if (length < 32 || cross < 32)
            throw new ArgumentException("长截图选区至少需要 32 × 32 像素。");
        if ((long)length * cross > MaximumPixels)
            return StitchResult.Limit;
        var image = frame.Format == PixelFormats.Bgr32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
        uint[] signature = Signature(image, length, cross);
        if (previous is null)
        {
            previous = signature;
            image.Freeze();
            strips.Add(image);
            Width = frame.PixelWidth;
            Height = frame.PixelHeight;
            return StitchResult.Added;
        }
        if (previous.Length != length || (Horizontal ? Height : Width) != cross)
            throw new ArgumentException("滚动期间的捕获尺寸发生变化。");
        if (previous.AsSpan().SequenceEqual(signature))
            return StitchResult.Unchanged;
        int offset = FindOffset(previous, signature);
        if (offset == 0)
            return StitchResult.NoMatch;
        int newLength = (Horizontal ? Width : Height) + offset;
        if (newLength > MaximumAxis || (long)newLength * cross > MaximumPixels)
            return StitchResult.Limit;
        var part = new WriteableBitmap(new CroppedBitmap(image, Horizontal
            ? new Int32Rect(length - offset, 0, offset, cross)
            : new Int32Rect(0, length - offset, cross, offset)));
        part.Freeze();
        strips.Add(part);
        previous = signature;
        Width = Horizontal ? newLength : cross;
        Height = Horizontal ? cross : newLength;
        return StitchResult.Added;
    }

    private uint[] Signature(BitmapSource image, int length, int cross)
    {
        int stride = image.PixelWidth * 4;
        byte[] pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        var signature = new uint[length];
        int step = Math.Max(1, cross / 30);
        for (int along = 0; along < length; along++)
        {
            uint hash = 2166136261;
            for (int across = step / 2; across < cross; across += step)
            {
                int index = Horizontal ? across * stride + 4 * along : along * stride + 4 * across;
                uint color = (uint)((pixels[index] & 248) | (pixels[index + 1] & 248) << 8 | (pixels[index + 2] & 248) << 16);
                hash = unchecked((hash ^ color) * 16777619);
            }
            signature[along] = hash;
        }
        return signature;
    }

    private static int FindOffset(uint[] earlier, uint[] current)
    {
        int best = 0;
        double score = 0, alternative = 0;
        var detail = new HashSet<uint>();
        for (int offset = 1; offset <= earlier.Length - Math.Max(24, earlier.Length / 4); offset++)
        {
            int overlap = earlier.Length - offset;
            int matches = 0;
            detail.Clear();
            for (int i = 0; i < overlap; i++)
            {
                uint row = earlier[offset + i];
                if (row != current[i])
                    continue;
                matches++;
                if (detail.Count < 5) detail.Add(row);
            }
            if (detail.Count < 5)
                continue;
            double candidate = (double)matches / overlap;
            if (candidate > score)
            {
                (score, alternative) = (candidate, score);
                best = offset;
            }
            else
                alternative = Math.Max(alternative, candidate);
        }
        return score >= .91 && score - alternative >= .006 ? best : 0;
    }

    public BitmapSource Finish()
    {
        if (strips.Count == 0)
            throw new InvalidOperationException("没有可用的长截图。");
        var result = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgr32, null);
        int x = 0, y = 0;
        foreach (var strip in strips)
        {
            int stride = strip.PixelWidth * 4;
            var bytes = new byte[stride * strip.PixelHeight];
            strip.CopyPixels(bytes, stride, 0);
            result.WritePixels(new Int32Rect(0, 0, strip.PixelWidth, strip.PixelHeight), bytes, stride, x, y);
            if (Horizontal) x += strip.PixelWidth;
            else y += strip.PixelHeight;
        }
        result.Freeze();
        return result;
    }
}
