using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Yxrl.Capture;

internal interface AnimatedRecording : IDisposable
{
    void Frame(byte[] pixels, long duration);
    void Complete();
}

internal sealed class GifRecording : AnimatedRecording
{
    private readonly BinaryWriter writer;
    private readonly int width, height;
    private long elapsed;
    private long lastDelay;

    public GifRecording(string path, int width, int height)
    {
        this.width = width;
        this.height = height;
        writer = new BinaryWriter(File.Create(path));
        try
        {
            writer.Write("GIF89a"u8);
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write(new byte[] { 0x70, 0, 0 });
            writer.Write(new byte[] { 0x21, 0xff, 0x0b });
            writer.Write("NETSCAPE2.0"u8);
            writer.Write(new byte[] { 3, 1, 0, 0, 0 });
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    public void Frame(byte[] pixels, long duration)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        var gif = new GifBitmapEncoder();
        gif.Frames.Add(BitmapFrame.Create(bitmap));
        using var frame = new MemoryStream();
        gif.Save(frame);
        byte[] bytes = frame.ToArray();
        int globalBits = bytes[10] & 7;
        int globalLength = (bytes[10] & 128) != 0 ? PaletteLength(globalBits) : 0;
        int descriptor = 13 + globalLength;
        while (bytes[descriptor] == 0x21)
            descriptor = SkipBlocks(bytes, descriptor + 2);
        if (bytes[descriptor] != 0x2c)
            throw new IOException("Windows 未生成 GIF 图像块。");
        byte imageFlags = bytes[descriptor + 9];
        bool local = (imageFlags & 128) != 0;
        int paletteBits = local ? imageFlags & 7 : globalBits;
        int paletteStart = local ? descriptor + 10 : 13;
        int paletteLength = PaletteLength(paletteBits);
        int dataStart = descriptor + 10 + (local ? paletteLength : 0);
        if (!local && globalLength == 0)
            throw new IOException("Windows 未生成 GIF 调色板。");
        int dataEnd = SkipBlocks(bytes, dataStart + 1);

        elapsed += duration;
        long next = Math.Max(lastDelay + 2, (elapsed + 50_000) / 100_000);
        writer.Write(new byte[] { 0x21, 0xf9, 4, 4 });
        writer.Write((ushort)Math.Min(ushort.MaxValue, next - lastDelay));
        writer.Write((ushort)0);
        writer.Write(bytes, descriptor, 9);
        writer.Write((byte)(128 | (imageFlags & 64) | paletteBits));
        writer.Write(bytes, paletteStart, paletteLength);
        writer.Write(bytes, dataStart, dataEnd - dataStart);
        lastDelay = next;
    }

    private static int PaletteLength(int sizeBits) => 3 << (sizeBits + 1);

    private static int SkipBlocks(byte[] bytes, int offset)
    {
        for (int size = bytes[offset++]; size != 0; size = bytes[offset++])
            offset += size;
        return offset;
    }

    public void Complete() => writer.Write((byte)0x3b);
    public void Dispose() => writer.Dispose();
}

internal sealed class WebpRecording : AnimatedRecording
{
    private readonly BinaryWriter writer;
    private readonly SKBitmap frame;
    private readonly int width, height;

    public WebpRecording(string path, int width, int height)
    {
        this.width = width;
        this.height = height;
        frame = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        BinaryWriter? output = null;
        try
        {
            writer = output = new BinaryWriter(File.Create(path));
            writer.Write("RIFF"u8);
            writer.Write(0u);
            writer.Write("WEBP"u8);
            Chunk("VP8X"u8, 10);
            writer.Write(2u);
            Write24(width - 1);
            Write24(height - 1);
            Chunk("ANIM"u8, 6);
            writer.Write(0u);
            writer.Write((ushort)0);
        }
        catch
        {
            output?.Dispose();
            frame.Dispose();
            throw;
        }
    }

    public void Frame(byte[] pixels, long duration)
    {
        Marshal.Copy(pixels, 0, frame.GetPixels(), pixels.Length);
        frame.NotifyPixelsChanged();
        using var encoded = frame.Encode(SKEncodedImageFormat.Webp, 86) ?? throw new IOException("WebP 编码失败。");
        byte[] bytes = encoded.ToArray();
        for (int offset = 12; offset + 8 < bytes.Length;)
        {
            var name = bytes.AsSpan(offset, 4);
            int length = BitConverter.ToInt32(bytes, offset + 4);
            int chunkLength = checked(8 + length + (length & 1));
            if (name.SequenceEqual("VP8 "u8) || name.SequenceEqual("VP8L"u8))
            {
                Chunk("ANMF"u8, 16 + chunkLength);
                Write24(0);
                Write24(0);
                Write24(width - 1);
                Write24(height - 1);
                Write24((int)Math.Clamp(Math.Round((double)duration / TimeSpan.TicksPerMillisecond), 1, 0xffffff));
                writer.Write((byte)2);
                writer.Write(bytes, offset, chunkLength);
                return;
            }
            offset += chunkLength;
        }
        throw new IOException("WebP 编码没有产生图像块。");
    }

    private void Chunk(ReadOnlySpan<byte> name, int length)
    {
        writer.Write(name);
        writer.Write(length);
    }

    private void Write24(int value)
    {
        writer.Write((byte)value);
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value >> 16));
    }

    public void Complete()
    {
        uint size = checked((uint)(writer.BaseStream.Position - 8));
        writer.Seek(4, SeekOrigin.Begin);
        writer.Write(size);
    }

    public void Dispose()
    {
        writer.Dispose();
        frame.Dispose();
    }
}
