using System.Runtime.InteropServices;

namespace AudioGroup.Interop;

/// <summary>
/// 设置系统默认音频设备的未公开 COM 接口（Windows 至今没有公开 API）。
/// 声明顺序参考 MIT 许可的 frgnca/AudioDeviceCmdlets 源码并做了交叉核对：
///   CLSID_CPolicyConfigClient = 870af99c-171d-4f9e-af0d-e63df40c2bc9
///   IID_IPolicyConfig         = f8679f50-850a-41cf-9c72-430f290290c8
///   SetDefaultEndpoint 位于 vtable 第 13 号槽（IUnknown 3 个 + 前 10 个方法）。
///
/// 注意：该接口不区分输入/输出，方向由传入的 deviceId 决定（Render 或 Capture 端点 ID）。
/// 前 10 个方法只是占位以保持 vtable 顺序，本程序绝不会调用它们。
/// </summary>
[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class CPolicyConfigClient { }

[Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, bool isDefault, IntPtr format);
    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, bool isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, bool visible);
}

/// <summary>Vista / Win7 上的旧版接口，方法表与上面完全一致，只是 IID 不同。</summary>
[Guid("568b9108-44bf-40b4-9006-86afe5b5a620"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigVista
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, bool isDefault, IntPtr format);
    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, bool isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, bool visible);
}

/// <summary>
/// 最后兜底：直接用 IUnknown 的 IID 取接口指针（AudioDeviceCmdlets 的兼容做法）。
/// 只要对象的方法表一致，第 13 号槽仍是 SetDefaultEndpoint。
/// </summary>
[Guid("00000000-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig10
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, bool isDefault, IntPtr format);
    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, bool isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, bool fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, bool visible);
}

/// <summary>
/// 对三个版本接口做 QueryInterface 回退，调用方只需关心 SetDefaultEndpoint。
/// 每次切换新建一个实例即可，避免长期持有 RCW。
/// </summary>
internal sealed class PolicyConfigClient
{
    private const int E_NOINTERFACE = unchecked((int)0x80004002);

    private readonly IPolicyConfig? _main;
    private readonly IPolicyConfigVista? _vista;
    private readonly IPolicyConfig10? _ten;

    /// <summary>实际命中的接口名，写进日志便于排查兼容性问题。</summary>
    public string InterfaceName { get; }

    public bool IsAvailable => _main is not null || _vista is not null || _ten is not null;

    public PolicyConfigClient()
    {
        var obj = new CPolicyConfigClient();

        _main = obj as IPolicyConfig;
        if (_main is not null)
        {
            InterfaceName = "IPolicyConfig";
            return;
        }

        _vista = obj as IPolicyConfigVista;
        if (_vista is not null)
        {
            InterfaceName = "IPolicyConfigVista";
            return;
        }

        _ten = obj as IPolicyConfig10;
        InterfaceName = _ten is not null ? "IUnknown(兜底)" : "不可用(E_NOINTERFACE)";
    }

    /// <summary>返回 HRESULT，0 表示成功。</summary>
    public int SetDefaultEndpoint(string deviceId, ERole role)
    {
        if (_main is not null) return _main.SetDefaultEndpoint(deviceId, role);
        if (_vista is not null) return _vista.SetDefaultEndpoint(deviceId, role);
        if (_ten is not null) return _ten.SetDefaultEndpoint(deviceId, role);
        return E_NOINTERFACE;
    }
}
