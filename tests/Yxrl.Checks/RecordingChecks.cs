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
        VerifyEnhancements(directory, clip, from, to);
        VerifyRecovery(directory);
        Console.WriteLine("通过：Windows H.264/AAC、剪辑、0.2x~2x 倍速、三档画质/进度叠加、镜像摄像头、动图、两轨独立调音、键鼠合成及未完成片段恢复。");
    }

    private static void VerifyEnhancements(string directory, RecordedClip clip, TimeSpan from, TimeSpan to)
    {
        Check(Mp4Writer.ComputeBitrate(1280, 720, 30, RecordingQuality.Compact) <
            Mp4Writer.ComputeBitrate(1280, 720, 30, RecordingQuality.Standard) &&
            Mp4Writer.ComputeBitrate(1280, 720, 30, RecordingQuality.Standard) <
            Mp4Writer.ComputeBitrate(1280, 720, 30, RecordingQuality.High), "录屏三档导出码率递增");

        var slowFile = Path.Combine(directory, "slow-02x.mp4");
        var slowEdits = RecordingEdits.Whole(clip) with
        {
            Speed = 0.2,
            Quality = RecordingQuality.Compact,
            HardwareAcceleration = true
        };
        RecordingExport.Save(clip, slowFile, RecordingFormat.Mp4, from, to, CancellationToken.None, edits: slowEdits);
        using (var reader = new ClipReader(clip with { Path = slowFile, Fps = 16, Duration = TimeSpan.FromMilliseconds(2600) }, false, 0))
        {
            var slowFrames = reader.Samples(false, TimeSpan.FromMilliseconds(2600).Ticks).Where(s => s.IsVideo).ToArray();
            Check(slowFrames.Length == 8 && slowFrames.Last().Time >= TimeSpan.FromMilliseconds(2000).Ticks,
                $"0.2x 慢放时序异常，共 {slowFrames.Length} 帧，末帧 {slowFrames.Last().Time}");
        }

        var croppedWithBar = RecordingEdits.Whole(clip) with
        {
            Crop = new(8, 12, 120, 100),
            Quality = RecordingQuality.High,
            ProgressStyle = RecordingProgressStyle.Bar
        };
        var barMp4 = Path.Combine(directory, "bar-crop.mp4");
        RecordingExport.Save(clip, barMp4, RecordingFormat.Mp4, from, to, CancellationToken.None, edits: croppedWithBar);
        using (var reader = new ClipReader(clip with { Path = barMp4, Width = 112, Height = 88, Duration = to - from }, false, 0))
        {
            var video = reader.Samples(false, (to - from).Ticks).Where(s => s.IsVideo).ToArray();
            var last = video.Last().Data;
            int p = (86 * 112 + 40) * 4;
            Check(video.Length == 8 && last.Length == 112 * 88 * 4 && last[p] > 180 && last[p + 2] < 110,
                "裁剪后底部蓝色进度条未正确烧录到 MP4");
        }

        var barWebp = Path.Combine(directory, "bar-crop.webp");
        RecordingExport.Save(clip, barWebp, RecordingFormat.Webp, from, to, CancellationToken.None,
            edits: croppedWithBar with { Quality = RecordingQuality.Compact });
        using (var codec = SKCodec.Create(barWebp))
        {
            Check(codec is not null && codec.FrameCount == 8 && codec.Info.Width == 112 && codec.Info.Height == 88,
                "带进度条裁剪 WebP 帧数或尺寸异常");
            using var bmp = new SKBitmap(codec!.Info);
            codec.GetPixels(bmp.Info, bmp.GetPixels(), new SKCodecOptions(codec.FrameCount - 1));
            var c = bmp.GetPixel(40, 86);
            Check(c.Blue > 200 && c.Red < 90, $"WebP 末帧底部进度条像素异常 {c}");
        }

        Check(RecordingProgressPainter.FormatClock(TimeSpan.FromSeconds(75).Ticks) == "01:15", "进度计时格式化异常");
        Check(Math.Abs(RecordingProgressPainter.ProgressFraction(50, 100) - 0.5) < 0.001, "进度比例计算异常");
        var badgeBuf = new byte[160 * 90 * 4];
        RecordingProgressPainter.Draw(badgeBuf, 160, 90, TimeSpan.FromSeconds(2).Ticks, TimeSpan.FromSeconds(5).Ticks,
            RecordingProgressStyle.Countdown);
        Check(badgeBuf.Skip(90 * 160 * 0).Where((v, i) => i % 4 == 3 && v > 0).Any(), "倒计时进度角标未绘制像素");

        // 摄像头镜像与尺寸验证：左半纯蓝(B=240), 右半纯红(R=240)
        const int sw = 200, sh = 120, cw = 40, ch = 30;
        var camPixels = new byte[cw * ch * 4];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int i = (y * cw + x) * 4;
                if (x < cw / 2) camPixels[i] = 240;
                else camPixels[i + 2] = 240;
                camPixels[i + 3] = 255;
            }
        var screenMirror = new byte[sw * sh * 4];
        var screenNormal = new byte[sw * sh * 4];
        RecordingCamera.OverlayFrame(screenMirror, sw, sh, camPixels, cw, ch,
            new CameraOptions(null!, 0, false, Mirror: true, Size: CameraSize.Small));
        RecordingCamera.OverlayFrame(screenNormal, sw, sh, camPixels, cw, ch,
            new CameraOptions(null!, 0, false, Mirror: false, Size: CameraSize.Large));
        // Small PiP (~18% of 200 = 36px wide, margin 12 => x in [152..188], y in [81..108])
        int leftSample = (95 * sw + 160) * 4;
        int rightSample = (95 * sw + 180) * 4;
        Check(screenMirror[leftSample + 2] > 180 && screenMirror[rightSample] > 180, "镜像开启后摄像头左右未翻转");
        Check(screenNormal[leftSample] > 180 || screenNormal[(95 * sw + 140) * 4] > 180, "关闭镜像与大尺寸摄像头叠加位置异常");

        var quickDir = Path.Combine(directory, "quick-paths");
        Directory.CreateDirectory(quickDir);
        var q1 = RecordingWindow.UniqueQuickSavePath("mp4", quickDir);
        File.WriteAllBytes(q1, [0]);
        var q2 = RecordingWindow.UniqueQuickSavePath("mp4", quickDir);
        Check(q1 != q2 && File.Exists(q1) && !File.Exists(q2) && q2.EndsWith(".mp4"), "快存/直接保存防重名路径异常");
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
