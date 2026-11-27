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
        Console.WriteLine("通过：Windows H.264/AAC 编码、MP4 读回、片段剪辑和画面裁剪、倍速、GIF 和 WebP 导出。");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("录屏验收失败：" + message);
    }
}
