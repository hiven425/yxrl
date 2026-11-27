using System.IO;
using System.Runtime.InteropServices;
using static Yxrl.Capture.MediaFoundation;

namespace Yxrl.Capture;

internal sealed record CameraDevice(string Name, string Link)
{
    public override string ToString() => Name;
}

internal enum CameraCorner { BottomRight, BottomLeft, TopRight, TopLeft }
internal enum CameraSize { Small, Medium, Large }
internal sealed record CameraOptions(CameraDevice Device, CameraCorner Corner, bool Circle, bool Mirror = true, CameraSize Size = CameraSize.Medium);

internal sealed class RecordingCamera : IDisposable
{
    private static readonly Guid SourceType = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
    private static readonly Guid FriendlyName = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
    private static readonly Guid SymbolicLink = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
    private static readonly Guid VideoCapture = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
    private const int VideoStream = -4;

    private readonly CameraOptions options;
    private readonly object gate = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MfCameraSource? device;
    private volatile CameraImage? image;
    private volatile bool stopped;
    private volatile Exception? failure;
    private sealed record CameraImage(int Width, int Height, byte[] Pixels);

    public RecordingCamera(CameraOptions options)
    {
        this.options = options;
        _ = Task.Factory.StartNew(Read, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public bool WaitForFrame(TimeSpan timeout)
    {
        try { ready.Task.WaitAsync(timeout).GetAwaiter().GetResult(); }
        catch (TimeoutException) { return false; }
        if (failure is { } error) throw error;
        return image is not null;
    }

    public static CameraDevice[] List()
    {
        using var media = new MediaFoundation();
        Check(MFCreateAttributes(out var attributes, 1));
        try
        {
            Check(attributes.SetGuid(SourceType, VideoCapture));
            Check(MFEnumDeviceSources(attributes, out nint devices, out int count));
            var result = new List<CameraDevice>();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var source = (MfAttributes)Marshal.GetObjectForIUnknown(Marshal.ReadIntPtr(devices, i * IntPtr.Size));
                    try
                    {
                        string name = Attribute(source, FriendlyName), link = Attribute(source, SymbolicLink);
                        if (name.Length > 0 && link.Length > 0)
                            result.Add(new(name, link));
                    }
                    finally { Release(source); }
                }
            }
            finally
            {
                for (int i = 0; i < count; i++)
                    Marshal.Release(Marshal.ReadIntPtr(devices, i * IntPtr.Size));
                Marshal.FreeCoTaskMem(devices);
            }
            return result.ToArray();
        }
        finally { Release(attributes); }
    }

    private static string Attribute(MfAttributes source, Guid id)
    {
        if (source.GetAllocatedString(id, out nint value, out int length) < 0)
            return "";
        try { return Marshal.PtrToStringUni(value, length) ?? ""; }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    private void Read()
    {
        try
        {
            using var media = new MediaFoundation();
            Check(MFCreateAttributes(out var attributes, 2));
            MfCameraSource source;
            try
            {
                Check(attributes.SetGuid(SourceType, VideoCapture));
                Check(attributes.SetString(SymbolicLink, options.Device.Link));
                Check(MFCreateDeviceSource(attributes, out source));
            }
            finally { Release(attributes); }
            bool attached = false;
            try
            {
                lock (gate)
                {
                    if (stopped) return;
                    device = source;
                    attached = true;
                }
                ReadFrames(source);
            }
            finally
            {
                lock (gate)
                {
                    if (device is not null || !attached) source.Shutdown();
                    device = null;
                    Release(source);
                }
            }
        }
        catch (Exception ex) when (ex is ExternalException or DllNotFoundException or EntryPointNotFoundException or IOException)
        {
            if (!stopped)
                failure = new InvalidOperationException("无法读取摄像头。请检查设备与 Windows 摄像头访问权限。", ex);
        }
        finally { ready.TrySetResult(); }
    }

    private void ReadFrames(MfCameraSource source)
    {
        Check(MFCreateAttributes(out var settings, 2));
        MfSourceReader reader;
        try
        {
            Check(settings.SetUInt32(VideoProcessing, 1));
            Check(settings.SetUInt32(new Guid("56b67165-219e-456d-a22e-2d3004c7fe56"), 1));
            Check(MFCreateSourceReaderFromMediaSource(source, settings, out reader));
        }
        finally { Release(settings); }
        try
        {
            SelectCameraSize(reader);
            Check(MFCreateMediaType(out var format));
            try
            {
                Check(format.SetGuid(Major, Video));
                Check(format.SetGuid(Subtype, Rgb32));
                Check(reader.SetCurrentMediaType(VideoStream, 0, format));
            }
            finally { Release(format); }
            Check(reader.GetCurrentMediaType(VideoStream, out var current));
            int width, height, stride;
            try
            {
                Check(current.GetUInt64(FrameSize, out long size));
                width = (int)(size >> 32);
                height = (int)size;
                stride = current.GetUInt32(Stride, out int pitch) >= 0 ? pitch : -width * 4;
            }
            finally { Release(current); }
            while (!stopped)
            {
                Check(reader.ReadSample(VideoStream, 0, out _, out int flags, out _, out var sample));
                try
                {
                    if ((flags & 3) != 0)
                        throw new IOException("摄像头已经停止传输画面。");
                    if (sample is null) continue;
                    image = new(width, height, ClipReader.ReadBytes(sample, width, height, stride));
                    ready.TrySetResult();
                }
                finally { Release(sample); }
            }
        }
        finally { Release(reader); }
    }

    private static void SelectCameraSize(MfSourceReader reader)
    {
        MfAttributes? chosen = null;
        int score = int.MaxValue;
        try
        {
            for (int i = 0; i < 96 && reader.GetNativeMediaType(VideoStream, i, out var type) >= 0; i++)
            {
                int scoreForType = type.GetUInt64(FrameSize, out long size) >= 0
                    ? Math.Abs((int)(size >> 32) - 640) + Math.Abs((int)size - 480) : int.MaxValue;
                if (scoreForType < score)
                {
                    Release(chosen);
                    chosen = type;
                    score = scoreForType;
                }
                else Release(type);
            }
            if (chosen is not null)
                reader.SetCurrentMediaType(VideoStream, 0, chosen);
        }
        finally { Release(chosen); }
    }

    public bool Overlay(byte[] screen, int width, int height)
    {
        if (failure is { } error) throw error;
        if (image is not { } frame) return false;
        return OverlayFrame(screen, width, height, frame.Pixels, frame.Width, frame.Height, options);
    }

    internal static bool OverlayFrame(byte[] screen, int width, int height, byte[] cameraPixels, int cameraWidth, int cameraHeight, CameraOptions options)
    {
        if (cameraWidth < 1 || cameraHeight < 1) return false;
        int inset = Math.Max(3, Math.Min(width, height) / 28);
        int w = options.Size switch
        {
            CameraSize.Small => Math.Min(width * 20 / 100, height * 24 / 100 * 4 / 3),
            CameraSize.Large => Math.Min(width * 36 / 100, height * 42 / 100 * 4 / 3),
            _ => Math.Min(width * 28 / 100, height / 3 * 4 / 3)
        };
        int h = options.Circle ? w : w * 3 / 4;
        if (w <= 0 || h <= 0) return false;
        bool right = options.Corner is CameraCorner.BottomRight or CameraCorner.TopRight;
        bool bottom = options.Corner is CameraCorner.BottomRight or CameraCorner.BottomLeft;
        int left = right ? width - w - inset : inset, top = bottom ? height - h - inset : inset;
        int sourceWidth = Math.Min(cameraWidth, cameraHeight * w / h);
        int sourceHeight = Math.Min(cameraHeight, cameraWidth * h / w);
        if (sourceWidth < 1 || sourceHeight < 1) return false;
        int sourceX = (cameraWidth - sourceWidth) / 2, sourceY = (cameraHeight - sourceHeight) / 2;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double px = ((x + .5) * 2 / w) - 1, py = ((y + .5) * 2 / h) - 1;
            double radius = px * px + py * py;
            if (options.Circle && radius > 1) continue;
            int dst = ((top + y) * width + left + x) * 4;
            bool border = options.Circle ? radius > .88 : x == 0 || y == 0 || x == w - 1 || y == h - 1;
            if (border)
            {
                screen[dst] = screen[dst + 1] = screen[dst + 2] = 246;
                continue;
            }
            int mappedX = x * sourceWidth / w;
            int sx = options.Mirror ? Math.Max(0, sourceWidth - 1 - mappedX) : mappedX;
            int src = ((sourceY + y * sourceHeight / h) * cameraWidth + sourceX + sx) * 4;
            screen[dst] = cameraPixels[src];
            screen[dst + 1] = cameraPixels[src + 1];
            screen[dst + 2] = cameraPixels[src + 2];
        }
        return true;
    }

    public void Dispose()
    {
        stopped = true;
        image = null;
        lock (gate)
        {
            device?.Shutdown();
            device = null;
        }
    }

    [DllImport("mf")] private static extern int MFEnumDeviceSources(MfAttributes attributes, out nint objects, out int count);
    [DllImport("mf")] private static extern int MFCreateDeviceSource(MfAttributes attributes, out MfCameraSource source);
    [DllImport("mfreadwrite")] private static extern int MFCreateSourceReaderFromMediaSource(MfCameraSource source, MfAttributes settings, out MfSourceReader reader);
}

[ComImport, Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface MfCameraSource
{
    [PreserveSig] void UnusedGetEvent();
    [PreserveSig] void UnusedBeginGetEvent();
    [PreserveSig] void UnusedEndGetEvent();
    [PreserveSig] void UnusedQueueEvent();
    [PreserveSig] void UnusedGetCharacteristics();
    [PreserveSig] void UnusedCreatePresentationDescriptor();
    [PreserveSig] void UnusedStart();
    [PreserveSig] void UnusedStop();
    [PreserveSig] void UnusedPause();
    [PreserveSig] int Shutdown();
}
