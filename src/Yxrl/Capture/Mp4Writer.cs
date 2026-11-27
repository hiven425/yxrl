using System.Diagnostics;
using System.Runtime.InteropServices;
using static Yxrl.Capture.MediaFoundation;

namespace Yxrl.Capture;

internal enum RecordingQuality { Compact, Standard, High }

internal sealed class Mp4Writer : IDisposable
{
    public const int AudioRate = 48000, AudioChannels = 2, AudioFrameBytes = AudioChannels * sizeof(short);
    internal const int MinimumEdge = 40;
    private readonly MfSinkWriter writer;
    private readonly int width, height, videoStream, audioStream = -1;
    private readonly byte[] nv12;

    public Mp4Writer(string path, int width, int height, int fps, bool withAudio = false,
        RecordingQuality quality = RecordingQuality.Standard, bool hardwareAcceleration = false)
    {
        if (width < MinimumEdge || height < MinimumEdge || (width & 1) != 0 || (height & 1) != 0 || fps < 1)
            throw new ArgumentException($"MP4 的边长至少为 {MinimumEdge} 像素且为偶数，帧率必须为正数。");
        this.width = width;
        this.height = height;
        nv12 = new byte[checked(width * height * 3 / 2)];
        writer = OpenWriter(path, width, height, fps, withAudio, quality, hardwareAcceleration, out videoStream, out audioStream);
    }

    private static MfSinkWriter OpenWriter(string path, int width, int height, int fps, bool withAudio,
        RecordingQuality quality, bool hardwareAcceleration, out int videoIndex, out int audioIndex)
    {
        if (hardwareAcceleration)
        {
            try
            {
                return OpenWriterCore(path, width, height, fps, withAudio, quality, true, out videoIndex, out audioIndex);
            }
            catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException)
            {
                Trace.TraceWarning("硬件加速 MP4 编码不可用，已自动回退到软件编码：{0}", ex.Message);
            }
        }
        return OpenWriterCore(path, width, height, fps, withAudio, quality, false, out videoIndex, out audioIndex);
    }

    private static MfSinkWriter OpenWriterCore(string path, int width, int height, int fps, bool withAudio,
        RecordingQuality quality, bool hardwareAcceleration, out int videoIndex, out int audioIndex)
    {
        audioIndex = -1;
        Check(MFCreateAttributes(out var settings, hardwareAcceleration ? 2 : 1));
        MfSinkWriter sink;
        try
        {
            Check(settings.SetUInt32(DisableThrottling, 1));
            if (hardwareAcceleration)
                Check(settings.SetUInt32(ReadWriteEnableHardwareTransforms, 1));
            Check(MFCreateSinkWriterFromURL(path, 0, settings, out sink));
        }
        finally
        {
            Release(settings);
        }
        try
        {
            Check(MFCreateMediaType(out var encodedVideo));
            try
            {
                SetVideoType(encodedVideo, H264, width, height, fps);
                Check(encodedVideo.SetUInt32(BitRate, ComputeBitrate(width, height, fps, quality)));
                Check(sink.AddStream(encodedVideo, out videoIndex));
            }
            finally
            {
                Release(encodedVideo);
            }

            if (withAudio)
            {
                Check(MFCreateMediaType(out var audio));
                try
                {
                    SetAudioType(audio, Aac);
                    Check(sink.AddStream(audio, out audioIndex));
                }
                finally
                {
                    Release(audio);
                }
            }

            Check(MFCreateMediaType(out var input));
            try
            {
                SetVideoType(input, Nv12, width, height, fps);
                Check(sink.SetInputMediaType(videoIndex, input, 0));
            }
            finally
            {
                Release(input);
            }
            if (withAudio)
            {
                Check(MFCreateMediaType(out var inputAudio));
                try
                {
                    SetAudioType(inputAudio, Pcm);
                    Check(sink.SetInputMediaType(audioIndex, inputAudio, 0));
                }
                finally
                {
                    Release(inputAudio);
                }
            }
            Check(sink.BeginWriting());
            return sink;
        }
        catch
        {
            Release(sink);
            throw;
        }
    }

    internal static int ComputeBitrate(int width, int height, int fps, RecordingQuality quality)
    {
        double pixelsPerSecond = width * (double)height * fps;
        return quality switch
        {
            RecordingQuality.Compact => (int)Math.Clamp(Math.Round(pixelsPerSecond * 0.55 / 4.0), 220_000, 10_000_000),
            RecordingQuality.High => (int)Math.Clamp(Math.Round(pixelsPerSecond * 1.65 / 4.0), 600_000, 42_000_000),
            _ => (int)Math.Clamp((long)width * height * fps / 4, 300_000, 28_000_000)
        };
    }

    private static void SetVideoType(MfAttributes type, Guid format, int width, int height, int fps)
    {
        Check(type.SetGuid(Major, Video));
        Check(type.SetGuid(Subtype, format));
        Check(type.SetUInt64(FrameSize, Pair(width, height)));
        Check(type.SetUInt64(FrameRate, Pair(fps, 1)));
        Check(type.SetUInt64(PixelAspectRatio, Pair(1, 1)));
        Check(type.SetUInt32(Interlace, 2)); // Progressive
        Check(type.SetUInt32(YuvMatrix, 2)); // BT.601, matching the BGR conversion below.
        if (format == Nv12)
            Check(type.SetUInt32(Stride, width));
    }

    internal static void SetAudioType(MfAttributes type, Guid format)
    {
        Check(type.SetGuid(Major, Audio));
        Check(type.SetGuid(Subtype, format));
        Check(type.SetUInt32(Channels, AudioChannels));
        Check(type.SetUInt32(SampleRate, AudioRate));
        Check(type.SetUInt32(BitsPerSample, 16));
        Check(type.SetUInt32(ChannelMask, 3));
        if (format == Pcm)
        {
            Check(type.SetUInt32(Alignment, AudioFrameBytes));
            Check(type.SetUInt32(AudioBytesPerSecond, AudioRate * AudioFrameBytes));
        }
        else
        {
            Check(type.SetUInt32(AudioBytesPerSecond, 16_000));
            Check(type.SetUInt32(AacPayload, 0));
            Check(type.SetUInt32(AacProfile, 0x29));
        }
    }

    public void Frame(byte[] bgr, long time, long duration)
    {
        int pitch = width * 4;
        for (int y = 0; y < height; y += 2)
        {
            int top = y * pitch, bottom = top + pitch;
            int luma = y * width, chroma = width * height + luma / 2;
            for (int x = 0; x < width; x += 2)
            {
                int i = top + x * 4, j = bottom + x * 4;
                nv12[luma + x] = Luma(bgr, i);
                nv12[luma + x + 1] = Luma(bgr, i + 4);
                nv12[luma + x + width] = Luma(bgr, j);
                nv12[luma + x + width + 1] = Luma(bgr, j + 4);
                int b = (bgr[i] + bgr[i + 4] + bgr[j] + bgr[j + 4]) / 4;
                int g = (bgr[i + 1] + bgr[i + 5] + bgr[j + 1] + bgr[j + 5]) / 4;
                int r = (bgr[i + 2] + bgr[i + 6] + bgr[j + 2] + bgr[j + 6]) / 4;
                nv12[chroma + x] = (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                nv12[chroma + x + 1] = (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
            }
        }
        Write(videoStream, nv12, nv12.Length, time, duration);
    }

    private static byte Luma(byte[] bgr, int i) => (byte)(((66 * bgr[i + 2] + 129 * bgr[i + 1] + 25 * bgr[i] + 128) >> 8) + 16);

    public void AudioFrames(byte[] pcm, int length, long time, long duration) =>
        Write(audioStream, pcm, length, time, duration);

    private void Write(int stream, byte[] bytes, int length, long time, long duration)
    {
        Check(MFCreateMemoryBuffer(length, out var buffer));
        try
        {
            Check(buffer.Lock(out nint data, out _, out _));
            try
            {
                Marshal.Copy(bytes, 0, data, length);
            }
            finally
            {
                Check(buffer.Unlock());
            }
            Check(buffer.SetCurrentLength(length));
            Check(MFCreateSample(out var sample));
            try
            {
                Check(sample.AddBuffer(buffer));
                Check(sample.SetSampleTime(time));
                Check(sample.SetSampleDuration(duration));
                Check(writer.WriteSample(stream, sample));
            }
            finally
            {
                Release(sample);
            }
        }
        finally
        {
            Release(buffer);
        }
    }

    public void Complete() => Check(writer.FinalizeWriter());
    public void Dispose() => Release(writer);
}
