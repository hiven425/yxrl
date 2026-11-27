using System.Runtime.InteropServices;
using static Yxrl.Capture.MediaFoundation;

namespace Yxrl.Capture;

internal sealed class RecordingAudio : IDisposable
{
    private readonly List<AudioEndpoint> endpoints = [];
    private long framesWritten;

    public RecordingAudio(bool system, bool microphone)
    {
        try
        {
            if (system) endpoints.Add(new AudioEndpoint(loopback: true));
            if (microphone) endpoints.Add(new AudioEndpoint(loopback: false));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Flush(Mp4Writer writer, long videoTicks, bool paused)
    {
        foreach (var endpoint in endpoints)
        {
            endpoint.Poll();
            if (paused)
                endpoint.Samples.Clear();
        }
        if (paused)
            return;

        long endFrame = videoTicks * Mp4Writer.AudioRate / TimeSpan.TicksPerSecond;
        while (framesWritten < endFrame)
        {
            int frameCount = (int)Math.Min(Mp4Writer.AudioRate / 10, endFrame - framesWritten);
            var mixed = new byte[frameCount * Mp4Writer.AudioFrameBytes];
            for (int i = 0; i < mixed.Length; i += 2)
            {
                int sum = 0;
                foreach (var endpoint in endpoints)
                    if (endpoint.Samples.TryDequeue(out short sample))
                        sum += sample;
                short value = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
                mixed[i] = (byte)value;
                mixed[i + 1] = (byte)(value >> 8);
            }
            long start = AudioTime(framesWritten);
            framesWritten += frameCount;
            writer.AudioFrames(mixed, mixed.Length, start, AudioTime(framesWritten) - start);
        }
    }

    private static long AudioTime(long samples) => samples * TimeSpan.TicksPerSecond / Mp4Writer.AudioRate;

    public void Dispose()
    {
        foreach (var endpoint in endpoints)
            endpoint.Dispose();
        endpoints.Clear();
    }

    private sealed class AudioEndpoint : IDisposable
    {
        private static readonly Guid EnumeratorClass = new("bcde0395-e52f-467c-8e3d-c4579291692e");
        private AudioClient? client;
        private AudioCapture? capture;
        private short[] packet = [];
        private bool started;
        public Queue<short> Samples { get; } = new();

        public AudioEndpoint(bool loopback)
        {
            try
            {
                var enumerator = (AudioEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClass)!)!;
                try
                {
                    Check(enumerator.GetDefaultAudioEndpoint(loopback ? 0 : 1, 1, out var device));
                    try
                    {
                        Check(device.Activate(typeof(AudioClient).GUID, 23, 0, out nint pointer));
                        try { client = (AudioClient)Marshal.GetObjectForIUnknown(pointer); }
                        finally { Marshal.Release(pointer); }
                    }
                    finally { Release(device); }
                }
                finally { Release(enumerator); }

                var format = new WaveFormat
                {
                    Format = 1, Channels = Mp4Writer.AudioChannels, Rate = Mp4Writer.AudioRate, Bits = 16,
                    Alignment = Mp4Writer.AudioFrameBytes, Average = Mp4Writer.AudioRate * Mp4Writer.AudioFrameBytes
                };
                const uint Convert = 0x80000000, HighQuality = 0x08000000, Loopback = 0x00020000;
                Check(client.Initialize(0, Convert | HighQuality | (loopback ? Loopback : 0), 2_000_000, 0, in format, 0));
                Check(client.GetService(typeof(AudioCapture).GUID, out nint service));
                try { capture = (AudioCapture)Marshal.GetObjectForIUnknown(service); }
                finally { Marshal.Release(service); }
                Check(client.Start());
                started = true;
            }
            catch (COMException ex)
            {
                Dispose();
                throw new InvalidOperationException(loopback ? "无法打开系统声音设备。" : "无法打开麦克风。请检查设备和 Windows 麦克风访问权限。", ex);
            }
        }

        public void Poll()
        {
            var service = capture!;
            Check(service.GetNextPacketSize(out int available));
            while (available > 0)
            {
                Check(service.GetBuffer(out nint data, out int frames, out int flags, out _, out _));
                try
                {
                    int samples = frames * Mp4Writer.AudioChannels;
                    if (samples > packet.Length)
                        packet = new short[samples];
                    if ((flags & 2) == 0)
                        Marshal.Copy(data, packet, 0, samples);
                    else
                        Array.Clear(packet, 0, samples);
                    for (int i = 0; i < samples; i++)
                        Samples.Enqueue(packet[i]);
                    while (Samples.Count > Mp4Writer.AudioRate * Mp4Writer.AudioChannels)
                        Samples.Dequeue();
                }
                finally
                {
                    Check(service.ReleaseBuffer(frames));
                }
                Check(service.GetNextPacketSize(out available));
            }
        }

        public void Dispose()
        {
            if (started) client!.Stop();
            Release(capture);
            Release(client);
            started = false;
            capture = null;
            client = null;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Format, Channels;
        public uint Rate, Average;
        public ushort Alignment, Bits, Extra;
    }

    [ComImport, Guid("a95664d2-9614-4f35-a746-de8db63617e6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface AudioEnumerator
    {
        [PreserveSig] void UnusedEnumAudioEndpoints();
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out AudioDevice device);
    }

    [ComImport, Guid("d666063f-1587-4e43-81f1-b948e807363f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface AudioDevice
    {
        [PreserveSig] int Activate(in Guid type, int context, nint activation, out nint result);
    }

    [ComImport, Guid("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface AudioClient
    {
        [PreserveSig] int Initialize(int share, uint flags, long bufferDuration, long period, in WaveFormat format, nint session);
        [PreserveSig] void UnusedGetBufferSize();
        [PreserveSig] void UnusedGetStreamLatency();
        [PreserveSig] void UnusedGetCurrentPadding();
        [PreserveSig] void UnusedIsFormatSupported();
        [PreserveSig] void UnusedGetMixFormat();
        [PreserveSig] void UnusedGetDevicePeriod();
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] void UnusedReset();
        [PreserveSig] void UnusedSetEventHandle();
        [PreserveSig] int GetService(in Guid type, out nint result);
    }

    [ComImport, Guid("c8adbd64-e71e-48a0-a4de-185c395cd317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface AudioCapture
    {
        [PreserveSig] int GetBuffer(out nint data, out int frames, out int flags, out long position, out long clock);
        [PreserveSig] int ReleaseBuffer(int frames);
        [PreserveSig] int GetNextPacketSize(out int frames);
    }
}
