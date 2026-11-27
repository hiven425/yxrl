using System.Diagnostics;
using System.IO;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed record RecordingOptions(int Fps, bool Cursor, bool SystemAudio, bool Microphone,
    bool MouseCues = false, bool KeyCues = false, CameraOptions? Camera = null, string? MicrophoneDevice = null,
    RecordingQuality Quality = RecordingQuality.Standard, bool HardwareAcceleration = false)
{
    public bool HasAudio => SystemAudio || Microphone;
}

internal sealed record RecordedClip(string Path, int Width, int Height, int Fps, TimeSpan Duration, bool HasAudio,
    RecordingTracks? Tracks = null, RecordingSession? Session = null, InputCue[]? Cues = null);

internal sealed class RegionRecording
{
    private readonly object gate = new();
    private readonly Stopwatch clock = new();
    private readonly List<InputCue> cues = [];
    private bool paused, stop, discard, interrupted;
    public RecordingSession Session { get; }
    public Task<RecordedClip?> Completion { get; }

    public RegionRecording(PixelRect area, RecordingOptions options, string folder)
    {
        Session = RecordingSession.Create(folder, area, options);
        Completion = Task.Factory.StartNew(() => Record(area, options), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public long? CueTime()
    {
        lock (gate) return !paused && !stop && clock.IsRunning ? clock.Elapsed.Ticks : null;
    }

    public void AddCue(InputCue cue)
    {
        lock (gate)
        {
            if (!paused && !stop && cues.Count < 15_000)
                cues.Add(cue);
        }
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

    public void Stop(bool abandon = false, bool interrupt = false)
    {
        lock (gate)
        {
            stop = true;
            discard |= abandon;
            interrupted |= interrupt;
            clock.Stop();
            Monitor.Pulse(gate);
        }
    }

    private RecordedClip? Record(PixelRect area, RecordingOptions options)
    {
        try
        {
            using var media = new MediaFoundation();
            Capture(area, options);
            lock (gate)
                if (discard || interrupted) return null;
            return Session.Parts().Any() ? RecordingAssembler.Prepare(Session) : null;
        }
        finally
        {
            bool abandoned;
            lock (gate) abandoned = discard;
            if (abandoned || !Session.Parts().Any())
                Session.Delete();
        }
    }

    private void Capture(PixelRect area, RecordingOptions options)
    {
        using var source = new ScreenPixels(area);
        using var camera = options.Camera is { } settings ? new RecordingCamera(settings) : null;
        if (camera is not null && !camera.WaitForFrame(TimeSpan.FromSeconds(6)))
            throw new InvalidOperationException("没有收到摄像头画面。请检查设备或选择其他摄像头。");
        using var audio = options.HasAudio ? new RecordingAudio(Session, options.MicrophoneDevice) : null;
        var pixels = new byte[source.Length];
        const long PartLimit = 3 * TimeSpan.TicksPerSecond, AudioLag = 100 * TimeSpan.TicksPerMillisecond;
        long lastCheck = -TimeSpan.TicksPerSecond, partStart = 0, duration = 0;
        int part = 0;
        bool wasPaused = false;
        Mp4Writer? sink = null;
        long interval = TimeSpan.TicksPerSecond / options.Fps;
        void Seal()
        {
            if (sink is not { } current) return;
            sink = null;
            using (current) current.Complete();
            using (var video = new FileStream(Session.PartFile(part), FileMode.Open, FileAccess.Write, FileShare.Read))
                video.Flush(flushToDisk: true);
            audio?.Flush(duration - AudioLag, durable: true);
            InputCue[] events;
            lock (gate)
            {
                events = cues.Where(cue => cue.Time < duration).ToArray();
                cues.RemoveAll(cue => cue.Time < duration);
            }
            Session.Seal(part, duration - partStart, events);
            partStart = duration;
            part++;
        }
        lock (gate)
            if (!paused && !stop) clock.Start();
        audio?.Resume(0);
        try
        {
            while (true)
            {
                long now;
                bool waiting;
                lock (gate)
                {
                    if (stop) break;
                    now = clock.Elapsed.Ticks;
                    waiting = paused;
                    if (waiting || now < duration)
                    {
                        Monitor.Wait(gate, waiting ? 25 : (int)Math.Clamp((duration - now) / TimeSpan.TicksPerMillisecond, 1, 25));
                        if (!waiting || stop) continue;
                    }
                }
                if (waiting)
                {
                    if (!wasPaused)
                    {
                        audio?.Flush(duration);
                        wasPaused = true;
                    }
                    audio?.DiscardPaused();
                    continue;
                }
                if (wasPaused)
                {
                    audio?.Resume(now);
                    wasPaused = false;
                }
                if (now - lastCheck >= TimeSpan.TicksPerSecond)
                {
                    if (DesktopFrame.SessionLocked() || !DesktopFrame.HasActiveDisplay())
                        throw new InvalidOperationException("Windows 桌面暂不可录制。可恢复已封存的片段。");
                    lastCheck = now;
                }

                if (duration - partStart >= PartLimit) Seal();
                long time = sink is null ? partStart : Math.Max(duration, now - now % interval);
                lock (gate)
                {
                    if (paused || stop) continue;
                    source.CopyFrame(pixels, options.Cursor);
                }
                sink ??= new Mp4Writer(Session.PartFile(part), area.Width, area.Height, options.Fps,
                    quality: options.Quality, hardwareAcceleration: options.HardwareAcceleration);
                camera?.Overlay(pixels, area.Width, area.Height);
                sink.Frame(pixels, time - partStart, interval);
                duration = time + interval;
                audio?.Flush(duration - AudioLag);
            }
            lock (gate)
                if (discard || duration <= partStart) return;
            if (!wasPaused && audio is not null)
                Thread.Sleep(75);
            audio?.Flush(duration, durable: true);
            Seal();
        }
        finally { sink?.Dispose(); }
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

    public static void Remove(string path)
    {
        try { File.Delete(path); }
        catch (IOException ex) { Trace.TraceWarning("无法删除录屏临时文件 {0}: {1}", path, ex.Message); }
        catch (UnauthorizedAccessException ex) { Trace.TraceWarning("无法删除录屏临时文件 {0}: {1}", path, ex.Message); }
    }
}
