using System.IO;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal enum RecordingFormat { Mp4, Gif, Webp }

internal sealed record RecordingEdits(PixelRect Crop, double Speed)
{
    public static RecordingEdits Whole(RecordedClip clip) => new(new(0, 0, clip.Width, clip.Height), 1);
    public long Time(long ticks) => (long)Math.Round(ticks / Speed);

    public byte[] Pixels(RecordedClip clip, byte[] source)
    {
        if (Crop == Whole(clip).Crop)
            return source;
        int stride = Crop.Width * 4;
        var image = new byte[checked(stride * Crop.Height)];
        for (int y = 0; y < Crop.Height; y++)
            Buffer.BlockCopy(source, ((Crop.Top + y) * clip.Width + Crop.Left) * 4, image, y * stride, stride);
        return image;
    }
}

internal static class RecordingExport
{
    public static void Save(RecordedClip clip, string path, RecordingFormat format, TimeSpan from, TimeSpan to,
        CancellationToken cancellation, IProgress<double>? progress = null, RecordingEdits? edits = null)
    {
        if (from < TimeSpan.Zero || to > clip.Duration || to - from < TimeSpan.FromTicks(TimeSpan.TicksPerSecond / clip.Fps))
            throw new ArgumentException("结束时间须晚于开始时间，且至少保留一帧。");
        var whole = RecordingEdits.Whole(clip);
        edits ??= whole;
        if (edits.Crop.Intersect(whole.Crop) != edits.Crop ||
            edits.Crop.Width < Mp4Writer.MinimumEdge || edits.Crop.Height < Mp4Writer.MinimumEdge ||
            (edits.Crop.Width & 1) != 0 || (edits.Crop.Height & 1) != 0 || !double.IsFinite(edits.Speed) || edits.Speed is < .25 or > 2)
            throw new ArgumentException($"裁剪后每条边至少保留 {Mp4Writer.MinimumEdge} 像素且为偶数，倍速须在 0.25 到 2 之间。");
        string stage = RecordingFiles.Staging(path);
        try
        {
            if (format == RecordingFormat.Mp4 && from == TimeSpan.Zero && to == clip.Duration && edits == whole)
                File.Copy(clip.Path, stage);
            else
            {
                using var media = new MediaFoundation();
                bool sound = clip.HasAudio && format == RecordingFormat.Mp4;
                using var reader = new ClipReader(clip, sound, from.Ticks);
                if (format == RecordingFormat.Mp4)
                    TrimVideo(clip, reader, stage, from.Ticks, to.Ticks, edits, cancellation, progress);
                else
                    ExportAnimation(clip, reader, stage, format, from.Ticks, to.Ticks, edits, cancellation, progress);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(stage, path, true);
        }
        finally
        {
            RecordingFiles.Remove(stage);
        }
    }

    private static void TrimVideo(RecordedClip clip, ClipReader reader, string path, long start, long end, RecordingEdits edits,
        CancellationToken cancellation, IProgress<double>? progress)
    {
        int fps = Math.Max(1, (int)Math.Round(clip.Fps * edits.Speed));
        using var writer = new Mp4Writer(path, edits.Crop.Width, edits.Crop.Height, fps, clip.HasAudio);
        int frames = 0;
        foreach (var frame in reader.Samples(clip.HasAudio, end))
        {
            cancellation.ThrowIfCancellationRequested();
            long first = Math.Max(start, frame.Time), last = Math.Min(end, frame.Time + frame.Duration);
            if (last <= first)
                continue;
            long time = edits.Time(first - start), duration = edits.Time(last - start) - time;
            if (frame.IsVideo)
            {
                writer.Frame(edits.Pixels(clip, frame.Data), time, duration);
                frames++;
                progress?.Report((double)(last - start) / (end - start));
            }
            else
                WriteTrimmedAudio(writer, frame, first, last, time, duration);
        }
        EnsureFrames(frames);
        writer.Complete();
    }

    private static void WriteTrimmedAudio(Mp4Writer writer, ClipSample frame, long first, long last, long time, long duration)
    {
        const int Rate = Mp4Writer.AudioRate, Block = Mp4Writer.AudioFrameBytes;
        static long Samples(long ticks) => ticks * Rate / TimeSpan.TicksPerSecond;
        int skip = (int)Samples(first - frame.Time);
        int count = Math.Min((int)Samples(last - frame.Time) - skip, frame.Data.Length / Block - skip);
        int output = (int)(Samples(time + duration) - Samples(time));
        if (count <= 0 || output <= 0)
            return;
        if (count == output)
        {
            if (skip > 0)
                Buffer.BlockCopy(frame.Data, skip * Block, frame.Data, 0, count * Block);
            writer.AudioFrames(frame.Data, count * Block, time, duration);
            return;
        }
        var resized = new byte[output * Block];
        for (int i = 0; i < output; i++)
        {
            int inputIndex = (skip + (int)((long)i * count / output)) * Block;
            Buffer.BlockCopy(frame.Data, inputIndex, resized, i * Block, Block);
        }
        writer.AudioFrames(resized, resized.Length, time, duration);
    }

    private static void ExportAnimation(RecordedClip clip, ClipReader reader, string path, RecordingFormat format,
        long start, long end, RecordingEdits edits, CancellationToken cancellation, IProgress<double>? progress)
    {
        using AnimatedRecording writer = format == RecordingFormat.Gif
            ? new GifRecording(path, edits.Crop.Width, edits.Crop.Height) : new WebpRecording(path, edits.Crop.Width, edits.Crop.Height);
        // Short animations retain timing while capping GIF at 20 and WebP at 30 frames/second.
        long interval = TimeSpan.TicksPerSecond / (format == RecordingFormat.Gif ? 20 : 30);
        byte[]? previous = null;
        long written = start;
        int frames = 0;
        foreach (var frame in reader.Samples(false, end))
        {
            cancellation.ThrowIfCancellationRequested();
            if (frame.Time + frame.Duration <= start)
                continue;
            if (previous is null)
                previous = frame.Data;
            else if (edits.Time(frame.Time - written) >= interval)
            {
                writer.Frame(edits.Pixels(clip, previous), edits.Time(frame.Time - start) - edits.Time(written - start));
                frames++;
                progress?.Report((double)(frame.Time - start) / (end - start));
                written = frame.Time;
                previous = frame.Data;
            }
        }
        if (previous is not null && end > written)
        {
            writer.Frame(edits.Pixels(clip, previous), edits.Time(end - start) - edits.Time(written - start));
            frames++;
        }
        EnsureFrames(frames);
        writer.Complete();
    }

    private static void EnsureFrames(int frames)
    {
        if (frames == 0)
            throw new IOException("剪辑范围内没有可导出的画面。");
    }
}
