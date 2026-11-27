using System.IO;
using System.Windows.Media.Imaging;
using SkiaSharp;
using Yxrl.Capture;

internal static class RecordingChecks
{
    internal static void Verify(string directory)
    {
        const int width = 128, height = 106, fps = 16;
        const long frameTime = TimeSpan.TicksPerSecond / fps;
        string file = Path.Combine(directory, "recording.mp4");
        using var media = new MediaFoundation();
        using (var writer = new Mp4Writer(file, width, height, fps, withAudio: true))
        {
            var pixels = new byte[width * height * 4];
            var audio = new byte[Mp4Writer.AudioRate * Mp4Writer.AudioFrameBytes / fps];
            for (int frame = 0; frame < fps; frame++)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * width + x) * 4;
                        pixels[i] = y >= height / 2 ? (byte)235 : (byte)23;
                        pixels[i + 1] = frame >= fps / 2 ? (byte)175 : (byte)42;
                        pixels[i + 2] = y < height / 2 ? (byte)226 : (byte)18;
                    }
                for (int i = 0; i < audio.Length; i += Mp4Writer.AudioFrameBytes)
                {
                    short sample = (short)(12_000 * Math.Sin(2 * Math.PI * 480 * (i / Mp4Writer.AudioFrameBytes) / Mp4Writer.AudioRate));
                    audio[i] = audio[i + 2] = (byte)sample;
                    audio[i + 1] = audio[i + 3] = (byte)(sample >> 8);
                }
                writer.Frame(pixels, frame * frameTime, frameTime);
                writer.AudioFrames(audio, audio.Length, frame * frameTime, frameTime);
            }
            writer.Complete();
        }

        var clip = new RecordedClip(file, width, height, fps, TimeSpan.FromSeconds(1), true);
        using (var reader = new ClipReader(clip, true, 0))
        {
            var all = reader.Samples(true, clip.Duration.Ticks).ToList();
            var video = all.Where(sample => sample.IsVideo).ToList();
            Check(video.Count == fps && video.Last().Time >= TimeSpan.FromMilliseconds(875).Ticks, "MP4 视频时序");
            Check(video[0].Data[(10 * width + 10) * 4 + 2] > 190 && video[0].Data[(60 * width + 10) * 4] > 190,
                "MP4 解码后的画面方向和颜色");
            Check(video.Last().Data[(10 * width + 10) * 4 + 1] > 145, "MP4 保留画面变化");
            Check(all.Any(sample => !sample.IsVideo && sample.Data.Any(value => value != 0)), "MP4 音轨的实际数据");
        }
        var from = TimeSpan.FromMilliseconds(250);
        var to = TimeSpan.FromMilliseconds(750);
        var trimmedFile = Path.Combine(directory, "clip.mp4");
        RecordingExport.Save(clip, trimmedFile, RecordingFormat.Mp4, from, to, CancellationToken.None);
        using (var reader = new ClipReader(clip with { Path = trimmedFile, Duration = to - from }, true, 0))
        {
            var samples = reader.Samples(true, (to - from).Ticks).ToList();
            Check(samples.Count(sample => sample.IsVideo) == fps / 2 && samples.Any(sample => !sample.IsVideo), "裁剪后的 MP4 画面和声音");
        }
        var gif = Path.Combine(directory, "clip.gif");
        RecordingExport.Save(clip, gif, RecordingFormat.Gif, from, to, CancellationToken.None);
        using (var stream = File.OpenRead(gif))
        {
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Check(decoder.Frames.Count == fps / 2 && decoder.Frames[0].PixelWidth == width, "GIF 动画帧");
        }
        var webp = Path.Combine(directory, "clip.webp");
        RecordingExport.Save(clip, webp, RecordingFormat.Webp, from, to, CancellationToken.None);
        using (var codec = SKCodec.Create(webp))
            Check(codec is not null && codec.FrameCount == fps / 2 && codec.Info.Height == height, "WebP 动画帧");
        var editedFile = Path.Combine(directory, "edited.mp4");
        var edits = new RecordingEdits(new(8, 54, 88, 102), 2);
        RecordingExport.Save(clip, editedFile, RecordingFormat.Mp4, from, to, CancellationToken.None, edits: edits);
        using (var reader = new ClipReader(clip with { Path = editedFile, Width = 80, Height = 48, Fps = 32,
            Duration = TimeSpan.FromMilliseconds(250) }, true, 0))
        {
            var samples = reader.Samples(true, TimeSpan.FromMilliseconds(260).Ticks).ToList();
            var frames = samples.Where(s => s.IsVideo).ToArray();
            Check(frames.Length == fps / 2 && frames.Last().Time < TimeSpan.FromMilliseconds(250).Ticks &&
                frames.All(s => s.Data.Length == 80 * 48 * 4 && s.Data[0] > 190) && samples.Any(s => !s.IsVideo),
                "裁画面和倍速后的 MP4 像素、时序和音轨");
        }
        VerifyRecovery(directory);
        Console.WriteLine("通过：Windows H.264/AAC、剪辑、动图、两轨独立调音、键鼠合成及未完成片段恢复。");
    }

    private static void VerifyRecovery(string directory)
    {
        string root = Path.Combine(directory, "recovery");
        const int width = 96, height = 64, fps = 24, frames = 12;
        const long partTime = TimeSpan.TicksPerSecond / 2, wholeTime = partTime * 2;
        var session = RecordingSession.Create(root, new(0, 0, width, height), new(fps, false, true, true));
        for (int part = 0; part < 2; part++)
        {
            using (var sink = new Mp4Writer(session.PartFile(part), width, height, fps))
                WriteVideo(sink, part);
            session.Seal(part, partTime, part == 0 ? [new(0, CueKind.Left, 28, 22), new(0, CueKind.Shortcut, 0, 0, "Ctrl+T")] : []);
        }
        void WriteVideo(Mp4Writer sink, int part)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) pixels[i + (part == 0 ? 2 : 1)] = 180;
            for (int i = 0; i < frames; i++)
                sink.Frame(pixels, i * TimeSpan.TicksPerSecond / fps, TimeSpan.TicksPerSecond / fps);
            sink.Complete();
        }
        File.WriteAllBytes(session.PartFile(2), [8, 9, 10]);
        WriteTone(session.RawSound(false), 510);
        WriteTone(session.RawSound(true), 930);
        var recovered = RecordingSession.Recoverable(root).Single();
        Check(recovered.Parts().Count() == 2, "恢复需要忽略未提交的末尾");
        var clip = RecordingAssembler.Prepare(recovered);
        Check(clip.Duration.Ticks == wholeTime && clip.Tracks?.System is not null && clip.Tracks.Microphone is not null,
            "恢复后的时长与原始音轨");
        using (var reader = new ClipReader(clip, false, 0))
        {
            var samples = reader.Samples(false, wholeTime).ToList();
            Check(samples.Count == 24 && samples.First().Data[2] > 150 && samples.Last().Data[1] > 150, "恢复两段画面可逐帧解码");
        }
        Check(RecordingInput.Label('A', false, false, false, true) is null &&
            RecordingInput.Label('A', true, false, false, false) == "Ctrl+A", "普通打字与快捷键动画的界限");
        var mix = Path.Combine(directory, "mix-adjusted.mp4");
        RecordingExport.Save(clip, mix, RecordingFormat.Mp4, TimeSpan.Zero, clip.Duration, CancellationToken.None,
            edits: RecordingEdits.Whole(clip) with { Mix = new(.5, 0) });
        using (var reader = new ClipReader(clip with { Path = mix }, true, 0))
        {
            var samples = reader.Samples(true, wholeTime).ToList();
            var audio = samples.Where(s => !s.IsVideo).SelectMany(s => s.Data).ToArray();
            Check(ToneLevel(audio, 510, 0, .8) is > 3400 and < 4900 && ToneLevel(audio, 930, 0, .8) < 200,
                "系统声音半音量、麦克风静音后独立混音");
            var original = samples.First(s => s.IsVideo).Data;
            Check(original.Where((value, i) => i % 4 == 0 && value > 90).Count() > 20, "合成后的键鼠动画画面");
        }
        session.Delete();
        Check(!RecordingSession.Recoverable(root).Any(), "显式删除后的恢复目录");
    }

    private static void WriteTone(string file, int frequency)
    {
        using var output = new BinaryWriter(File.Create(file));
        for (int i = 0; i < Mp4Writer.AudioRate; i++)
        {
            short value = (short)(8000 * Math.Sin(2 * Math.PI * frequency * i / Mp4Writer.AudioRate));
            output.Write(value);
            output.Write(value);
        }
    }

    internal static double ToneLevel(byte[] data, double frequency, double start, double end)
    {
        int first = (int)(start * Mp4Writer.AudioRate), last = Math.Min(data.Length / Mp4Writer.AudioFrameBytes, (int)(end * Mp4Writer.AudioRate));
        double re = 0, im = 0, step = 2 * Math.PI * frequency / Mp4Writer.AudioRate;
        for (int i = first; i < last; i++)
        {
            var sample = BitConverter.ToInt16(data, i * Mp4Writer.AudioFrameBytes);
            re += sample * Math.Cos(step * i);
            im += sample * Math.Sin(step * i);
        }
        return last > first ? 2 * Math.Sqrt(re * re + im * im) / (last - first) : 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("录屏验收失败：" + message);
    }
}
