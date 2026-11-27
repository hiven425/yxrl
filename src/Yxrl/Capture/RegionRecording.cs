using System.Diagnostics;
using System.IO;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed record RecordingOptions(int Fps, bool Cursor, bool SystemAudio, bool Microphone)
{
    public bool HasAudio => SystemAudio || Microphone;
}

internal sealed record RecordedClip(string Path, int Width, int Height, int Fps, TimeSpan Duration, bool HasAudio);

internal sealed class RegionRecording
{
    private readonly object gate = new();
    private readonly Stopwatch clock = new();
    private bool paused, stop, discard;
    public Task<RecordedClip?> Completion { get; }

    public RegionRecording(PixelRect area, RecordingOptions options, string path)
    {
        Completion = Task.Factory.StartNew(() => Record(area, options, path), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public TimeSpan Elapsed
    {
        get { lock (gate) return clock.Elapsed; }
    }

    public void Pause(bool value)
    {
        lock (gate)
        {
            if (stop)
                return;
            paused = value;
            if (paused) clock.Stop();
            else clock.Start();
            Monitor.Pulse(gate);
        }
    }

    public void Stop(bool abandon = false)
    {
        lock (gate)
        {
            stop = true;
            discard |= abandon;
            clock.Stop();
            Monitor.Pulse(gate);
        }
    }

    private RecordedClip? Record(PixelRect area, RecordingOptions options, string path)
    {
        bool keep = false;
        try
        {
            using var media = new MediaFoundation();
            long duration;
            using (var sink = new Mp4Writer(path, area.Width, area.Height, options.Fps, options.HasAudio))
                duration = Capture(area, options, sink);
            lock (gate)
                keep = duration > 0 && !discard;
            return keep ? new(path, area.Width, area.Height, options.Fps, TimeSpan.FromTicks(duration), options.HasAudio) : null;
        }
        finally
        {
            if (!keep) RecordingFiles.Remove(path);
        }
    }

    private long Capture(PixelRect area, RecordingOptions options, Mp4Writer sink)
    {
        using var source = new ScreenPixels(area);
        using var audio = options.HasAudio ? new RecordingAudio(options.SystemAudio, options.Microphone) : null;
        var pixels = new byte[source.Length];
        long next = 0, duration = 0, checkedAt = -TimeSpan.TicksPerSecond;
        long interval = TimeSpan.TicksPerSecond / options.Fps;
        lock (gate)
            if (!paused && !stop)
                clock.Start();

        while (true)
        {
            long now;
            lock (gate)
            {
                if (stop)
                    break;
                now = clock.Elapsed.Ticks;
                if (paused || now < next)
                {
                    bool flush = paused;
                    Monitor.Wait(gate, flush ? 25 : (int)Math.Clamp((next - now) / TimeSpan.TicksPerMillisecond, 1, 25));
                    if (!flush || stop)
                        continue;
                }
            }
            if (IsPaused())
            {
                audio?.Flush(sink, duration, paused: true);
                continue;
            }
            if (now - checkedAt >= TimeSpan.TicksPerSecond)
            {
                if (DesktopFrame.SessionLocked())
                    throw new InvalidOperationException("Windows 桌面已锁定，录制已停止。");
                checkedAt = now;
            }

            long time = duration == 0 ? 0 : Math.Max(next, now - now % interval);
            source.CopyFrame(pixels, options.Cursor);
            sink.Frame(pixels, time, interval);
            audio?.Flush(sink, time, paused: false);
            next = duration = time + interval;
        }

        lock (gate)
            if (discard || duration == 0)
                return 0;
        audio?.Flush(sink, duration, paused: false);
        sink.Complete();
        return duration;
    }

    private bool IsPaused()
    {
        lock (gate) return paused;
    }

    internal static bool EnsureDisplay()
    {
        if (DesktopFrame.SessionLocked())
            throw new InvalidOperationException("Windows 桌面已锁定，请解锁后重新录制。");
        return DesktopFrame.HasActiveDisplay();
    }
}

internal static class RecordingFiles
{
    public static string Staging(string target) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!,
        $".YXRL-{Guid.NewGuid():N}{Path.GetExtension(target)}");

    public static string TempClip() => Path.Combine(Path.GetTempPath(), $"YXRL-{Guid.NewGuid():N}.mp4");

    public static void Remove(string path)
    {
        try { File.Delete(path); }
        catch (IOException ex) { Trace.TraceWarning("无法删除录屏临时文件 {0}: {1}", path, ex.Message); }
        catch (UnauthorizedAccessException ex) { Trace.TraceWarning("无法删除录屏临时文件 {0}: {1}", path, ex.Message); }
    }
}
