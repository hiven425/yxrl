using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Yxrl.Desktop;

namespace Yxrl.Capture;

internal sealed record RecordingHeader(int Version, PixelRect Area, int Fps, bool SystemAudio, bool Microphone, DateTime Started);
internal sealed record RecordingPart(int Index, long Duration, InputCue[] Cues);
internal sealed record RecordingTracks(string? System, string? Microphone)
{
    public bool Available => System is not null || Microphone is not null;
}

internal sealed class RecordingSession(string folder, RecordingHeader header)
{
    private const int Version = 1;
    public string Folder { get; } = folder;
    public RecordingHeader Header { get; } = header;
    public string Video => Path.Combine(Folder, "回放.mp4");

    public static RecordingSession Create(string root, PixelRect area, RecordingOptions options)
    {
        var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var header = new RecordingHeader(Version, area, options.Fps, options.SystemAudio, options.Microphone, DateTime.UtcNow);
        var recording = new RecordingSession(folder, header);
        try
        {
            Commit(Path.Combine(folder, "session.json"), header);
            return recording;
        }
        catch
        {
            recording.Delete();
            throw;
        }
    }

    public static IEnumerable<RecordingSession> Recoverable(string root)
    {
        if (!Directory.Exists(root))
            yield break;
        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _) ||
                (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                continue;
            var header = Read<RecordingHeader>(Path.Combine(folder, "session.json"));
            if (header is { Version: Version, Fps: 5 or 16 or 24 or 30 or 60 } &&
                header.Area.Width >= Mp4Writer.MinimumEdge && header.Area.Height >= Mp4Writer.MinimumEdge &&
                (header.Area.Width & 1) == 0 && (header.Area.Height & 1) == 0)
            {
                var session = new RecordingSession(folder, header);
                if (session.Parts().Any())
                    yield return session;
            }
        }
    }

    public string PartFile(int index) => Path.Combine(Folder, $"part-{index:D6}.mp4");
    private string PartIndex(int index) => Path.Combine(Folder, $"part-{index:D6}.json");
    public string RawSound(bool mic) => Path.Combine(Folder, mic ? "microphone.pcm" : "system.pcm");
    public string Sound(bool mic) => Path.Combine(Folder, mic ? "microphone.wav" : "system.wav");

    public void Seal(int index, long duration, InputCue[] cues) => Commit(PartIndex(index), new RecordingPart(index, duration, cues));

    public IEnumerable<RecordingPart> Parts()
    {
        for (int i = 0; File.Exists(PartFile(i)) && Read<RecordingPart>(PartIndex(i)) is { } part; i++)
        {
            if (part.Index != i || part.Duration <= 0 || part.Cues is null)
                yield break;
            yield return part;
        }
    }

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void Commit<T>(string target, T value)
    {
        string stage = target + ".tmp";
        try
        {
            using (var output = File.Create(stage))
            {
                JsonSerializer.Serialize(output, value);
                output.Flush(flushToDisk: true);
            }
            File.Move(stage, target, true);
        }
        finally
        {
            RecordingFiles.Remove(stage);
        }
    }

    public void Delete()
    {
        try
        {
            if (!Directory.Exists(Folder) || (File.GetAttributes(Folder) & FileAttributes.ReparsePoint) != 0)
                return;
            // Only session-owned, flat files belong to the recovery store.
            foreach (var file in Directory.EnumerateFiles(Folder))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("part-", StringComparison.Ordinal) || name.StartsWith(".YXRL-", StringComparison.Ordinal) ||
                    name is "session.json" or "session.json.tmp" or "回放.mp4" or "system.pcm" or "microphone.pcm" or "system.wav" or "microphone.wav")
                    RecordingFiles.Remove(file);
            }
            Directory.Delete(Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("清理录屏会话失败：{0}", ex.Message);
        }
    }
}
