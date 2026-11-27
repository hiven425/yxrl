using System.IO;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal enum RecordingFormat { Mp4, Gif, Webp }

internal sealed record RecordingEdits(PixelRect Crop, double Speed, RecordingMix? Mix = null, bool MouseCues = true, bool KeyCues = true,
    RecordingQuality Quality = RecordingQuality.Standard, RecordingProgressStyle ProgressStyle = RecordingProgressStyle.None,
    bool HardwareAcceleration = false)
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
            (edits.Crop.Width & 1) != 0 || (edits.Crop.Height & 1) != 0 || !double.IsFinite(edits.Speed) || edits.Speed is < .2 or > 2)
            throw new ArgumentException($"裁剪后每条边至少保留 {Mp4Writer.MinimumEdge} 像素且为偶数，倍速须在 0.2 到 2 之间。");
        if (edits.Mix is { } mix && (!(mix.System >= 0 && mix.System <= 1) || !(mix.Microphone >= 0 && mix.Microphone <= 1)))
            throw new ArgumentException("录屏音量须在 0% 至 100% 之间。");
        string stage = RecordingFiles.Staging(path);
        try
        {
            bool hasCues = clip.Cues?.Any(cue => cue.Kind == CueKind.Shortcut ? edits.KeyCues : edits.MouseCues) == true;
            if (format == RecordingFormat.Mp4 && from == TimeSpan.Zero && to == clip.Duration &&
                edits.Crop == whole.Crop && edits.Speed == 1 && !hasCues && clip.Tracks?.Available != true &&
                edits.Quality == RecordingQuality.Standard && edits.ProgressStyle == RecordingProgressStyle.None && !edits.HardwareAcceleration)
                File.Copy(clip.Path, stage);
            else
            {
                using var media = new MediaFoundation();
                bool sound = clip.HasAudio && format == RecordingFormat.Mp4 && clip.Tracks?.Available != true;
                using var reader = new ClipReader(clip, sound, from.Ticks);
                using var effects = hasCues ? new CuePainter(new(clip.Cues!)) : null;
                if (format == RecordingFormat.Mp4)
                    TrimVideo(clip, reader, stage, from.Ticks, to.Ticks, edits, sound, effects, cancellation, progress);
                else
                    ExportAnimation(clip, reader, stage, format, from.Ticks, to.Ticks, edits, effects, cancellation, progress);
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
        bool embeddedAudio, CuePainter? effects, CancellationToken cancellation, IProgress<double>? progress)
    {
        int fps = Math.Max(1, (int)Math.Round(clip.Fps * edits.Speed));
        using var mixer = clip.Tracks is { Available: true } tracks
            ? new RecordingMixer(tracks, edits.Mix ?? new(), start, edits.Speed) : null;
        using var writer = new Mp4Writer(path, edits.Crop.Width, edits.Crop.Height, fps, embeddedAudio || mixer is not null,
            edits.Quality, edits.HardwareAcceleration);
        int frames = 0;
        long span = end - start;
        foreach (var frame in reader.Samples(embeddedAudio, end))
        {
            cancellation.ThrowIfCancellationRequested();
            long first = Math.Max(start, frame.Time), last = Math.Min(end, frame.Time + frame.Duration);
            if (last <= first)
                continue;
            long time = edits.Time(first - start), duration = edits.Time(last - start) - time;
            if (frame.IsVideo)
            {
                effects?.Draw(frame.Data, clip.Width, clip.Height, first, edits.MouseCues, edits.KeyCues);
                var outputPixels = edits.Pixels(clip, frame.Data);
                RecordingProgressPainter.Draw(outputPixels, edits.Crop.Width, edits.Crop.Height, first - start, span, edits.ProgressStyle);
                writer.Frame(outputPixels, time, duration);
                mixer?.WriteThrough(writer, time + duration);
                frames++;
                progress?.Report((double)(last - start) / span);
            }
            else
                WriteTrimmedAudio(writer, frame, first, last, time, duration);
        }
        EnsureFrames(frames);
        mixer?.WriteThrough(writer, edits.Time(end - start));
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
        long start, long end, RecordingEdits edits, CuePainter? effects, CancellationToken cancellation, IProgress<double>? progress)
    {
        int webpQuality = edits.Quality switch
        {
            RecordingQuality.Compact => 68,
            RecordingQuality.High => 95,
            _ => 86
        };
        int capFps = (format, edits.Quality) switch
        {
            (RecordingFormat.Gif, RecordingQuality.Compact) => 12,
            (RecordingFormat.Gif, RecordingQuality.High) => 25,
            (RecordingFormat.Gif, _) => 20,
            (RecordingFormat.Webp, RecordingQuality.Compact) => 18,
            _ => 30
        };
        using AnimatedRecording writer = format == RecordingFormat.Gif
            ? new GifRecording(path, edits.Crop.Width, edits.Crop.Height)
            : new WebpRecording(path, edits.Crop.Width, edits.Crop.Height, webpQuality);
        long interval = TimeSpan.TicksPerSecond / capFps;
        byte[]? previous = null;
        long written = start;
        int frames = 0;
        long span = end - start;
        void WriteFrame(byte[] pixels, long until)
        {
            effects?.Draw(pixels, clip.Width, clip.Height, written, edits.MouseCues, edits.KeyCues);
            var outputPixels = edits.Pixels(clip, pixels);
            RecordingProgressPainter.Draw(outputPixels, edits.Crop.Width, edits.Crop.Height, Math.Max(0, written - start), span, edits.ProgressStyle);
            writer.Frame(outputPixels, edits.Time(until - start) - edits.Time(written - start));
            frames++;
        }
        foreach (var frame in reader.Samples(false, end))
        {
            cancellation.ThrowIfCancellationRequested();
            if (frame.Time + frame.Duration <= start)
                continue;
            if (previous is null)
                previous = frame.Data;
            else if (edits.Time(frame.Time - written) >= interval)
            {
                WriteFrame(previous, frame.Time);
                progress?.Report((double)(frame.Time - start) / (end - start));
                written = frame.Time;
                previous = frame.Data;
            }
        }
        if (previous is not null && end > written)
            WriteFrame(previous, end);
        EnsureFrames(frames);
        writer.Complete();
    }

    private static void EnsureFrames(int frames)
    {
        if (frames == 0)
            throw new IOException("剪辑范围内没有可导出的画面。");
    }
}
