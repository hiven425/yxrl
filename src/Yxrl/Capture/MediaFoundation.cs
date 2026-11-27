using System.Runtime.InteropServices;

namespace Yxrl.Capture;

internal sealed class MediaFoundation : IDisposable
{
    public static readonly Guid Major = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid Interlace = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid BitRate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid Stride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid YuvMatrix = new("3e23d450-2c75-4d25-a00e-b91670d12327");
    public static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid H264 = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid Nv12 = new("3231564e-0000-0010-8000-00aa00389b71");
    public static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    public static readonly Guid VideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static readonly Guid DisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    public static readonly Guid ReadWriteEnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid Audio = new("73647561-0000-0010-8000-00aa00389b71");
    public static readonly Guid Pcm = new("00000001-0000-0010-8000-00aa00389b71");
    public static readonly Guid Aac = new("00001610-0000-0010-8000-00aa00389b71");
    public static readonly Guid Channels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    public static readonly Guid SampleRate = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid BitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    public static readonly Guid AudioBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    public static readonly Guid Alignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static readonly Guid AacPayload = new("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
    public static readonly Guid AacProfile = new("7632f0e6-9538-4d61-acda-ea29c8c14456");
    public static readonly Guid ChannelMask = new("55fb5765-644a-4caf-8479-938983bb1588");

    public MediaFoundation() => Check(MFStartup(0x20070, 0));
    public void Dispose() => MFShutdown();

    internal static long Pair(int high, int low) => (long)high << 32 | (uint)low;
    internal static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    internal static void Release(object? value)
    {
        if (value is not null)
            Marshal.ReleaseComObject(value);
    }

    [DllImport("mfplat")] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat")] private static extern int MFShutdown();
    [DllImport("mfplat")] internal static extern int MFCreateMediaType(out MfAttributes type);
    [DllImport("mfplat")] internal static extern int MFCreateAttributes(out MfAttributes attributes, int initialSize);
    [DllImport("mfplat")] internal static extern int MFCreateMemoryBuffer(int capacity, out MfBuffer buffer);
    [DllImport("mfplat")] internal static extern int MFCreateSample(out MfSample sample);
    [DllImport("mfreadwrite", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int MFCreateSinkWriterFromURL(string file, nint byteStream, MfAttributes? attrs, out MfSinkWriter writer);
    [DllImport("mfreadwrite", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int MFCreateSourceReaderFromURL(string file, MfAttributes? attrs, out MfSourceReader reader);
}

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface MfAttributes
{
    [PreserveSig] void UnusedGetItem();
    [PreserveSig] void UnusedGetItemType();
    [PreserveSig] void UnusedCompareItem();
    [PreserveSig] void UnusedCompare();
    [PreserveSig] int GetUInt32(in Guid key, out int value);
    [PreserveSig] int GetUInt64(in Guid key, out long value);
    [PreserveSig] void UnusedGetDouble();
    [PreserveSig] void UnusedGetGuid();
    [PreserveSig] void UnusedGetStringLength();
    [PreserveSig] void UnusedGetString();
    [PreserveSig] int GetAllocatedString(in Guid key, out nint text, out int length);
    [PreserveSig] void UnusedGetBlobSize();
    [PreserveSig] void UnusedGetBlob();
    [PreserveSig] void UnusedGetAllocatedBlob();
    [PreserveSig] void UnusedGetUnknown();
    [PreserveSig] void UnusedSetItem();
    [PreserveSig] void UnusedDeleteItem();
    [PreserveSig] void UnusedDeleteAllItems();
    [PreserveSig] int SetUInt32(in Guid key, int value);
    [PreserveSig] int SetUInt64(in Guid key, long value);
    [PreserveSig] void UnusedSetDouble();
    [PreserveSig] int SetGuid(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
}

[ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface MfBuffer
{
    [PreserveSig] int Lock(out nint pixels, out int capacity, out int length);
    [PreserveSig] int Unlock();
    [PreserveSig] int GetCurrentLength(out int length);
    [PreserveSig] int SetCurrentLength(int length);
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface MfSample
{
    // IMFSample inherits the 30 attributes slots in the Windows SDK.
    [PreserveSig] void UnusedGetItem();
    [PreserveSig] void UnusedGetItemType();
    [PreserveSig] void UnusedCompareItem();
    [PreserveSig] void UnusedCompare();
    [PreserveSig] void UnusedGetUInt32();
    [PreserveSig] void UnusedGetUInt64();
    [PreserveSig] void UnusedGetDouble();
    [PreserveSig] void UnusedGetGuid();
    [PreserveSig] void UnusedGetStringLength();
    [PreserveSig] void UnusedGetString();
    [PreserveSig] void UnusedGetAllocatedString();
    [PreserveSig] void UnusedGetBlobSize();
    [PreserveSig] void UnusedGetBlob();
    [PreserveSig] void UnusedGetAllocatedBlob();
    [PreserveSig] void UnusedGetUnknown();
    [PreserveSig] void UnusedSetItem();
    [PreserveSig] void UnusedDeleteItem();
    [PreserveSig] void UnusedDeleteAllItems();
    [PreserveSig] void UnusedSetUInt32();
    [PreserveSig] void UnusedSetUInt64();
    [PreserveSig] void UnusedSetDouble();
    [PreserveSig] void UnusedSetGuid();
    [PreserveSig] void UnusedSetString();
    [PreserveSig] void UnusedSetBlob();
    [PreserveSig] void UnusedSetUnknown();
    [PreserveSig] void UnusedLockStore();
    [PreserveSig] void UnusedUnlockStore();
    [PreserveSig] void UnusedGetCount();
    [PreserveSig] void UnusedGetItemByIndex();
    [PreserveSig] void UnusedCopyAllItems();
    [PreserveSig] void UnusedGetSampleFlags();
    [PreserveSig] void UnusedSetSampleFlags();
    [PreserveSig] void UnusedGetSampleTime();
    [PreserveSig] int SetSampleTime(long time);
    [PreserveSig] int GetSampleDuration(out long duration);
    [PreserveSig] int SetSampleDuration(long duration);
    [PreserveSig] void UnusedGetBufferCount();
    [PreserveSig] void UnusedGetBufferByIndex();
    [PreserveSig] int ConvertToContiguousBuffer(out MfBuffer buffer);
    [PreserveSig] int AddBuffer(MfBuffer buffer);
}

[ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface MfSinkWriter
{
    // IMFMediaType and IMFAttributes share the inherited interface pointer.
    [PreserveSig] int AddStream(MfAttributes mediaType, out int index);
    [PreserveSig] int SetInputMediaType(int stream, MfAttributes mediaType, nint parameters);
    [PreserveSig] int BeginWriting();
    [PreserveSig] int WriteSample(int stream, MfSample sample);
    [PreserveSig] void UnusedSendStreamTick();
    [PreserveSig] void UnusedPlaceMarker();
    [PreserveSig] void UnusedNotifyEndOfSegment();
    [PreserveSig] void UnusedFlush();
    [PreserveSig] int FinalizeWriter();
}

[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface MfSourceReader
{
    [PreserveSig] void UnusedGetStreamSelection();
    [PreserveSig] int SetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] bool select);
    [PreserveSig] int GetNativeMediaType(int stream, int index, out MfAttributes mediaType);
    [PreserveSig] int GetCurrentMediaType(int stream, out MfAttributes mediaType);
    [PreserveSig] int SetCurrentMediaType(int stream, nint reserved, MfAttributes mediaType);
    [PreserveSig] int SetCurrentPosition(in Guid format, in MfPosition value);
    [PreserveSig] int ReadSample(int stream, int control, out int actualStream, out int flags, out long time, out MfSample? sample);
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct MfPosition
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public long Ticks;
}
