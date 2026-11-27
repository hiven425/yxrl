using System.Buffers.Binary;
using System.IO;

namespace Yxrl.Capture;

internal sealed record RecordingMix(double System = 1, double Microphone = 1);

internal sealed class RecordingMixer : IDisposable
{
    private const int Block = Mp4Writer.AudioFrameBytes, WaveHeader = 44, FramesPerChunk = Mp4Writer.AudioRate / 10;
    private readonly FileStream? system, microphone;
    private readonly RecordingMix mix;
    private readonly double speed;
    private readonly long first;
    private readonly byte[] output = new byte[FramesPerChunk * Block],
        systemData = new byte[(FramesPerChunk * 2 + 2) * Block],
        microphoneData = new byte[(FramesPerChunk * 2 + 2) * Block];
    private long written;

    public RecordingMixer(RecordingTracks tracks, RecordingMix mix, long start, double speed)
    {
        this.mix = mix;
        this.speed = speed;
        first = RecordingAudio.Frames(start);
        system = tracks.System is not null && mix.System > 0 ? File.OpenRead(tracks.System) : null;
        try { microphone = tracks.Microphone is not null && mix.Microphone > 0 ? File.OpenRead(tracks.Microphone) : null; }
        catch { system?.Dispose(); throw; }
    }

    public void WriteThrough(Mp4Writer writer, long ticks)
    {
        long end = RecordingAudio.Frames(ticks);
        while (written < end)
        {
            int count = (int)Math.Min(end - written, FramesPerChunk);
            long from = SampleAt(written);
            int wanted = ((int)(SampleAt(written + count - 1) - from) + 1) * Block;
            Read(system, systemData, from, wanted);
            Read(microphone, microphoneData, from, wanted);
            for (int i = 0; i < count; i++)
            {
                int input = (int)(SampleAt(written + i) - from) * Block;
                for (int channel = 0; channel < Block; channel += sizeof(short))
                {
                    double a = BinaryPrimitives.ReadInt16LittleEndian(systemData.AsSpan(input + channel, 2)) * mix.System;
                    double b = BinaryPrimitives.ReadInt16LittleEndian(microphoneData.AsSpan(input + channel, 2)) * mix.Microphone;
                    short value = (short)Math.Clamp(Math.Round(a + b), short.MinValue, short.MaxValue);
                    BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(i * Block + channel, 2), value);
                }
            }
            long start = RecordingAudio.Time(written);
            written += count;
            writer.AudioFrames(output, count * Block, start, RecordingAudio.Time(written) - start);
        }
    }

    private long SampleAt(long destination) => first + (long)(destination * speed);

    private static void Read(FileStream? file, byte[] data, long index, int length)
    {
        int read = 0;
        if (file is not null)
        {
            file.Position = WaveHeader + index * Block;
            read = file.ReadAtLeast(data.AsSpan(0, length), length, throwOnEndOfStream: false);
        }
        data.AsSpan(read, length - read).Clear();
    }

    public void Dispose()
    {
        system?.Dispose();
        microphone?.Dispose();
    }
}
