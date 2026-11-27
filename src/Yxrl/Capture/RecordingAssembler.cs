using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static Yxrl.Capture.MediaFoundation;

namespace Yxrl.Capture;

internal static class RecordingAssembler
{
    public static RecordedClip Prepare(RecordingSession session)
    {
        var parts = session.Parts().ToArray();
        if (parts.Length == 0)
            throw new IOException("没有已完成的录屏片段。");
        var header = session.Header;
        long duration = parts.Sum(part => part.Duration);
        var clip = new RecordedClip(session.Video, header.Area.Width, header.Area.Height, header.Fps,
            TimeSpan.FromTicks(duration), false, Session: session, Cues: parts.SelectMany(part => part.Cues).OrderBy(cue => cue.Time).ToArray());
        using var media = new MediaFoundation();
        if (parts.Length == 1)
            CopyVideo(session.PartFile(0), clip.Path);
        else
            JoinVideo(session, clip, parts);
        var tracks = new RecordingTracks(header.SystemAudio ? PrepareSound(session, false, duration) : null,
            header.Microphone ? PrepareSound(session, true, duration) : null);
        return clip with { Tracks = tracks, HasAudio = tracks.Available };
    }

    private static void CopyVideo(string source, string destination)
    {
        string staging = RecordingFiles.Staging(destination);
        try
        {
            File.Copy(source, staging);
            File.Move(staging, destination, true);
        }
        finally { RecordingFiles.Remove(staging); }
    }

    private static void JoinVideo(RecordingSession session, RecordedClip clip, RecordingPart[] parts)
    {
        string staging = RecordingFiles.Staging(clip.Path);
        try
        {
            try
            {
                Remux(session, staging, parts);
            }
            catch (COMException)
            {
                RecordingFiles.Remove(staging);
                JoinDecoded(session, clip, staging, parts);
            }
            File.Move(staging, clip.Path, true);
        }
        finally { RecordingFiles.Remove(staging); }
    }

    private static void Remux(RecordingSession session, string destination, RecordingPart[] parts)
    {
        const int VideoStream = -4;
        Check(MFCreateSinkWriterFromURL(destination, 0, null, out var writer));
        try
        {
            int target = -1;
            long offset = 0;
            foreach (var part in parts)
            {
                Check(MFCreateSourceReaderFromURL(session.PartFile(part.Index), null, out var reader));
                try
                {
                    if (target < 0)
                    {
                        Check(reader.GetNativeMediaType(VideoStream, 0, out var type));
                        try
                        {
                            Check(writer.AddStream(type, out target));
                            Check(writer.SetInputMediaType(target, type, 0));
                            Check(writer.BeginWriting());
                        }
                        finally { Release(type); }
                    }
                    bool first = true;
                    long origin = 0;
                    while (true)
                    {
                        Check(reader.ReadSample(VideoStream, 0, out _, out int flags, out long time, out var sample));
                        try
                        {
                            if ((flags & 2) != 0) break;
                            if ((flags & 1) != 0) throw new IOException("录屏片段无法读取。");
                            if (sample is null) continue;
                            if (first)
                            {
                                origin = time;
                                first = false;
                            }
                            Check(sample.SetSampleTime(offset + time - origin));
                            Check(writer.WriteSample(target, sample));
                        }
                        finally { Release(sample); }
                    }
                    if (first) throw new IOException("录屏片段没有可读的视频帧。");
                }
                finally { Release(reader); }
                offset += part.Duration;
            }
            Check(writer.FinalizeWriter());
        }
        finally { Release(writer); }
    }

    private static void JoinDecoded(RecordingSession session, RecordedClip clip, string path, RecordingPart[] parts)
    {
        using var writer = new Mp4Writer(path, clip.Width, clip.Height, clip.Fps);
        long offset = 0;
        foreach (var part in parts)
        {
            using var reader = new ClipReader(clip with { Path = session.PartFile(part.Index) }, false, 0);
            int frames = 0;
            foreach (var sample in reader.Samples(false, part.Duration))
            {
                writer.Frame(sample.Data, offset + sample.Time, Math.Min(sample.Duration, part.Duration - sample.Time));
                frames++;
            }
            if (frames == 0) throw new IOException("录屏片段没有可读的视频帧。");
            offset += part.Duration;
        }
        writer.Complete();
    }

    internal static string? PrepareSound(RecordingSession session, bool mic, long duration)
    {
        string source = session.RawSound(mic);
        if (!File.Exists(source))
            return null;
        string target = session.Sound(mic), staging = RecordingFiles.Staging(target);
        long wantedSize = RecordingAudio.Frames(duration) * Mp4Writer.AudioFrameBytes;
        if (wantedSize > uint.MaxValue - 36)
            throw new IOException("单条音轨超过 WAV 回放上限。");
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = new BinaryWriter(File.Create(staging), Encoding.UTF8))
            {
                output.Write("RIFF"u8);
                output.Write((uint)(36 + wantedSize));
                output.Write("WAVEfmt "u8);
                output.Write(16);
                output.Write((ushort)1);
                output.Write((ushort)Mp4Writer.AudioChannels);
                output.Write(Mp4Writer.AudioRate);
                output.Write(Mp4Writer.AudioRate * Mp4Writer.AudioFrameBytes);
                output.Write((ushort)Mp4Writer.AudioFrameBytes);
                output.Write((ushort)16);
                output.Write("data"u8);
                output.Write((uint)wantedSize);
                var buffer = new byte[64 * 1024];
                long remaining = wantedSize;
                while (remaining > 0)
                {
                    int chunk = (int)Math.Min(remaining, buffer.Length);
                    int read = input.ReadAtLeast(buffer.AsSpan(0, chunk), chunk, throwOnEndOfStream: false);
                    buffer.AsSpan(read, chunk - read).Clear();
                    output.Write(buffer, 0, chunk);
                    remaining -= chunk;
                }
            }
            File.Move(staging, target, true);
            return target;
        }
        finally { RecordingFiles.Remove(staging); }
    }
}
