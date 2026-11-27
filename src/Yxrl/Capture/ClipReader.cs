using System.IO;
using System.Runtime.InteropServices;
using static Yxrl.Capture.MediaFoundation;

namespace Yxrl.Capture;

internal sealed record ClipSample(bool IsVideo, byte[] Data, long Time, long Duration);

internal sealed class ClipReader : IDisposable
{
    private const int AllStreams = -2, FirstVideo = -4, FirstAudio = -3, AnyStream = -2;
    private readonly MfSourceReader source;
    private readonly RecordedClip clip;
    private readonly int videoStride;
    private int videoIndex = -1;

    public ClipReader(RecordedClip clip, bool audio, long start)
    {
        this.clip = clip;
        Check(MFCreateAttributes(out var settings, 1));
        try
        {
            Check(settings.SetUInt32(VideoProcessing, 1));
            Check(MFCreateSourceReaderFromURL(clip.Path, settings, out source));
        }
        finally
        {
            Release(settings);
        }
        try
        {
            Check(source.SetStreamSelection(AllStreams, false));
            Check(source.SetStreamSelection(FirstVideo, true));
            Check(MFCreateMediaType(out var format));
            try
            {
                Check(format.SetGuid(Major, Video));
                Check(format.SetGuid(Subtype, Rgb32));
                Check(source.SetCurrentMediaType(FirstVideo, 0, format));
                if (audio)
                {
                    Mp4Writer.SetAudioType(format, Pcm);
                    Check(source.SetStreamSelection(FirstAudio, true));
                    Check(source.SetCurrentMediaType(FirstAudio, 0, format));
                }
            }
            finally
            {
                Release(format);
            }
            Check(source.GetCurrentMediaType(FirstVideo, out var decoded));
            try
            {
                videoStride = decoded.GetUInt32(Stride, out int pitch) >= 0 ? pitch : -clip.Width * 4;
            }
            finally
            {
                Release(decoded);
            }
            // The stream's actual index comes from the first video sample, even in a file with audio.
            if (audio)
            {
                Check(source.ReadSample(FirstVideo, 0, out videoIndex, out _, out _, out var probe));
                Release(probe);
            }
            if (start > 0 || audio)
                Check(source.SetCurrentPosition(Guid.Empty, new MfPosition { Type = 20, Ticks = start })); // VT_I8
        }
        catch
        {
            Release(source);
            throw;
        }
    }

    public IEnumerable<ClipSample> Samples(bool audio, long end)
    {
        bool videoEnded = false, audioEnded = !audio;
        while (!videoEnded || !audioEnded)
        {
            int selection = videoEnded ? FirstAudio : audioEnded ? FirstVideo : AnyStream;
            Check(source.ReadSample(selection, 0, out int stream, out int flags, out long time, out var sample));
            bool video = !audio || stream == videoIndex;
            try
            {
                if ((flags & 1) != 0)
                    throw new IOException("Windows 视频解码器停止读取。");
                if ((flags & 2) != 0 || time >= end)
                {
                    if (video) videoEnded = true;
                    else audioEnded = true;
                    continue;
                }
                if (sample is null || (video ? videoEnded : audioEnded))
                    continue;
                var data = ReadBytes(sample, video);
                long fallback = video ? TimeSpan.TicksPerSecond / clip.Fps :
                    (long)data.Length * TimeSpan.TicksPerSecond / (Mp4Writer.AudioRate * Mp4Writer.AudioFrameBytes);
                yield return new(video, data, time, sample.GetSampleDuration(out long length) >= 0 && length > 0 ? length : fallback);
            }
            finally
            {
                Release(sample);
            }
        }
    }

    private byte[] ReadBytes(MfSample sample, bool video)
    {
        Check(sample.ConvertToContiguousBuffer(out var buffer));
        try
        {
            Check(buffer.Lock(out nint bytes, out _, out int length));
            try
            {
                if (!video)
                {
                    var pcm = new byte[length];
                    Marshal.Copy(bytes, pcm, 0, length);
                    return pcm;
                }
                int rowBytes = clip.Width * 4, pitch = Math.Abs(videoStride);
                if (pitch < rowBytes || (long)(clip.Height - 1) * pitch + rowBytes > length)
                    throw new IOException("视频帧尺寸与录制源不一致。");
                var pixels = new byte[rowBytes * clip.Height];
                if (videoStride == rowBytes)
                    Marshal.Copy(bytes, pixels, 0, pixels.Length);
                else
                    for (int y = 0; y < clip.Height; y++)
                        Marshal.Copy(bytes + (videoStride > 0 ? y : clip.Height - 1 - y) * pitch,
                            pixels, y * rowBytes, rowBytes);
                return pixels;
            }
            finally
            {
                Check(buffer.Unlock());
            }
        }
        finally
        {
            Release(buffer);
        }
    }

    public void Dispose() => Release(source);
}
