using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static Yxrl.Capture.MediaFoundation;

namespace Yxrl.Capture;

internal sealed record RecordingMicrophone(string? Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed class RecordingAudio : IDisposable
{
    private static readonly Guid EnumeratorClass = new("bcde0395-e52f-467c-8e3d-c4579291692e");
    private static readonly PropertyKey FriendlyName = new() { Set = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
    private readonly List<AudioEndpoint> endpoints = [];
    private long framesWritten, mediaAnchor, captureAnchor;

    public RecordingAudio(RecordingSession session, string? microphone = null)
    {
        try
        {
            if (session.Header.SystemAudio) endpoints.Add(new AudioEndpoint(loopback: true, session.RawSound(false)));
            if (session.Header.Microphone) endpoints.Add(new AudioEndpoint(loopback: false, session.RawSound(true), microphone));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static AudioEnumerator Enumerator() =>
        (AudioEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClass)!)!;

    public static RecordingMicrophone[] Microphones()
    {
        var found = new List<RecordingMicrophone> { new(null, "Windows 默认麦克风") };
        var enumerator = Enumerator();
        try
        {
            Check(enumerator.EnumAudioEndpoints(1, 1, out var devices)); // Active capture endpoints
            try
            {
                Check(devices.GetCount(out int count));
                for (int i = 0; i < count; i++)
                {
                    Check(devices.Item(i, out var device));
                    try
                    {
                        Check(device.GetId(out nint id));
                        try
                        {
                            Check(device.OpenPropertyStore(0, out var properties));
                            try
                            {
                                Check(properties.GetValue(FriendlyName, out var v));
                                try
                                {
                                    if (v.Type == 31 && Marshal.PtrToStringUni(v.Value) is { } name &&
                                        Marshal.PtrToStringUni(id) is { } identifier)
                                        found.Add(new(identifier, name));
                                }
                                finally { PropVariantClear(ref v); }
                            }
                            finally { Release(properties); }
                        }
                        finally { Marshal.FreeCoTaskMem(id); }
                    }
                    finally { Release(device); }
                }
            }
            finally { Release(devices); }
        }
        finally { Release(enumerator); }
        return found.ToArray();
    }

    public void Resume(long mediaTicks)
    {
        foreach (var endpoint in endpoints)
        {
            endpoint.Discard();
        }
        captureAnchor = CaptureTicks();
        mediaAnchor = mediaTicks;
    }

    public void DiscardPaused()
    {
        foreach (var endpoint in endpoints)
            endpoint.Discard();
    }

    public void Flush(long videoTicks, bool durable = false)
    {
        foreach (var endpoint in endpoints)
            endpoint.Poll(mediaAnchor, captureAnchor);
        long endFrame = Frames(videoTicks);
        while (framesWritten < endFrame)
        {
            int frameCount = (int)Math.Min(Mp4Writer.AudioRate / 10, endFrame - framesWritten);
            foreach (var endpoint in endpoints)
                endpoint.Write(framesWritten, frameCount);
            framesWritten += frameCount;
        }
        if (durable)
            foreach (var endpoint in endpoints)
                endpoint.Sync();
    }

    internal static long Frames(long ticks) => ticks * Mp4Writer.AudioRate / TimeSpan.TicksPerSecond;
    internal static long Time(long samples) => samples * TimeSpan.TicksPerSecond / Mp4Writer.AudioRate;
    private static long CaptureTicks() => (long)(Stopwatch.GetTimestamp() * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));

    public void Dispose()
    {
        foreach (var endpoint in endpoints)
            endpoint.Dispose();
        endpoints.Clear();
    }

    private sealed class AudioEndpoint : IDisposable
    {
        private sealed record Packet(long First, short[] Samples)
        {
            public long End => First + Samples.Length / Mp4Writer.AudioChannels;
        }

        private readonly Queue<Packet> packets = new();
        private readonly byte[] block = new byte[Mp4Writer.AudioRate / 10 * Mp4Writer.AudioFrameBytes];
        private FileStream? output;
        private AudioClient? client;
        private AudioCapture? capture;
        private bool started;
        private long lastFrame = long.MinValue;

        public AudioEndpoint(bool loopback, string path, string? deviceId = null)
        {
            try
            {
                var enumerator = Enumerator();
                try
                {
                    Check(deviceId is null ? enumerator.GetDefaultAudioEndpoint(loopback ? 0 : 1, 1, out var device) :
                        enumerator.GetDevice(deviceId, out device));
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
                output = File.Create(path);
            }
            catch (Exception ex)
            {
                Dispose();
                if (ex is not COMException) throw;
                throw new InvalidOperationException(loopback ? "无法打开系统声音设备。" : "无法打开麦克风。请检查设备和 Windows 麦克风访问权限。", ex);
            }
        }

        public void Discard()
        {
            Poll(0, 0, keep: false);
            packets.Clear();
            lastFrame = long.MinValue;
        }

        public void Poll(long mediaAnchor, long captureAnchor, bool keep = true)
        {
            var service = capture!;
            Check(service.GetNextPacketSize(out int available));
            while (available > 0)
            {
                Check(service.GetBuffer(out nint data, out int frames, out int flags, out _, out long qpc));
                try
                {
                    if (keep && frames > 0)
                    {
                        if ((flags & 4) != 0 || qpc == 0)
                            qpc = CaptureTicks() - Time(frames);
                        long first = Frames(mediaAnchor + qpc - captureAnchor);
                        if (lastFrame != long.MinValue && Math.Abs(first - lastFrame) < 48)
                            first = lastFrame;
                        first = Math.Max(first, lastFrame);
                        lastFrame = first + frames;
                        var samples = new short[frames * Mp4Writer.AudioChannels];
                        if ((flags & 2) == 0)
                            Marshal.Copy(data, samples, 0, samples.Length);
                        packets.Enqueue(new(first, samples));
                    }
                }
                finally
                {
                    Check(service.ReleaseBuffer(frames));
                }
                Check(service.GetNextPacketSize(out available));
            }
        }

        public void Write(long start, int frames)
        {
            int length = frames * Mp4Writer.AudioFrameBytes;
            long end = start + frames;
            Array.Clear(block, 0, length);
            while (packets.TryPeek(out var packet) && packet.First < end)
            {
                long from = Math.Max(start, packet.First), to = Math.Min(end, packet.End);
                if (to > from)
                    Buffer.BlockCopy(packet.Samples, (int)(from - packet.First) * Mp4Writer.AudioFrameBytes, block,
                        (int)(from - start) * Mp4Writer.AudioFrameBytes, (int)(to - from) * Mp4Writer.AudioFrameBytes);
                if (packet.End > end)
                    break;
                packets.Dequeue();
            }
            output!.Write(block, 0, length);
        }

        public void Sync() => output!.Flush(flushToDisk: true);

        public void Dispose()
        {
            output?.Dispose();
            output = null;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Set;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyValue
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Value;
    }

    [DllImport("ole32")] private static extern int PropVariantClear(ref PropertyValue value);

    [ComImport, Guid("a95664d2-9614-4f35-a746-de8db63617e6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface AudioEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint stateMask, out AudioDevices devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out AudioDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out AudioDevice device);
    }

    [ComImport, Guid("0bd7a1be-7a1a-44db-8397-cc5392387b5e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface AudioDevices
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out AudioDevice device);
    }

    [ComImport, Guid("d666063f-1587-4e43-81f1-b948e807363f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface AudioDevice
    {
        [PreserveSig] int Activate(in Guid type, int context, nint activation, out nint result);
        [PreserveSig] int OpenPropertyStore(int mode, out AudioProperties properties);
        [PreserveSig] int GetId(out nint id);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface AudioProperties
    {
        [PreserveSig] void UnusedGetCount();
        [PreserveSig] void UnusedGetAt();
        [PreserveSig] int GetValue(in PropertyKey key, out PropertyValue value);
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
