using System.Runtime.InteropServices;

namespace AudioGroup.Interop;

/// <summary>数据流方向：0 = 播放输出(Render)，1 = 录音输入(Capture)。</summary>
internal enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2
}

/// <summary>
/// Windows 为每个方向分别维护 3 套默认设备。
/// 只设置 eConsole 会导致会议类软件仍走旧设备，所以一次切换必须三者全设。
/// </summary>
internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

internal static class DeviceState
{
    /// <summary>DEVICE_STATE_ACTIVE，只枚举当前可用的端点。</summary>
    public const int Active = 0x00000001;
}

/// <summary>PROPERTYKEY：属性集 GUID + 属性 ID。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;

    public PropertyKey(Guid formatId, int propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}

/// <summary>
/// PROPVARIANT 的内存布局（x64 下共 24 字节）。
/// 本程序只读取 VT_LPWSTR，其余字段仅用于占位以保证结构大小与 COM 一致。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct PropVariant
{
    public ushort ValueType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Pointer;   // VT_LPWSTR 时指向 Unicode 字符串
    public int HighPart;
    public int Padding;
}

internal static class PropertyKeys
{
    /// <summary>PKEY_Device_FriendlyName = {A45C254E-DF1C-4EFD-8020-67D146A850E0}, PID 14。</summary>
    public static readonly PropertyKey DeviceFriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
}

internal static class CoreAudioGuids
{
    /// <summary>CLSID_MMDeviceEnumerator。</summary>
    public static readonly Guid MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
}

// 以下代码是 Windows Core Audio API 的 COM 互操作声明。
// [ComImport] 类由 CLR 负责 CoCreateInstance；接口的 vtable 从 IUnknown 的 3 个方法之后开始，
// 因此这里只列业务方法，且顺序必须与 SDK 中的顺序完全一致。

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int Item(int index, out IMMDevice device);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    // Activate 本程序不使用，仅保留槽位；用 IntPtr 避免额外的封送复杂度。
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr instance);
    [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore store);
    [PreserveSig] int GetId(out IntPtr id);
    [PreserveSig] int GetState(out int state);
}

[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetAt(int index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}

/// <summary>
/// 设备插拔 / 默认设备变化通知。实现类由托管代码提供，CLR 自动生成 CCW。
/// </summary>
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    [PreserveSig] int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}
