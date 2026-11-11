using System.Runtime.InteropServices;
using AudioGroup.Interop;
using AudioGroup.Models;

namespace AudioGroup.Core;

/// <summary>一个活动音频端点的快照。</summary>
internal sealed class AudioEndpoint
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsRender { get; init; }
    public string FlowText => IsRender ? "输出" : "输入";

    public override string ToString() => Name;
}

/// <summary>设备解析结果：成功时 Id 有效，Message 可能是需要提示用户的告警。</summary>
internal sealed record ResolveResult(bool Success, string Id, string Message);

/// <summary>
/// 负责枚举活动端点、读取友好名称、按「ID 精确优先 / 名称模糊兜底」解析设备引用，
/// 以及回读系统当前默认设备和注册设备变化通知。
/// </summary>
internal sealed class DeviceCatalog : IDisposable
{
    private const int STGM_READ = 0;
    private const ushort VT_LPWSTR = 31;

    private readonly Logger _logger;
    private readonly IMMDeviceEnumerator _enumerator;

    // 必须长期持有引用，否则 COM 回调对象的 CCW 会被 GC 回收。
    private IMMNotificationClient? _registeredClient;
    private bool _disposed;

    public DeviceCatalog(Logger logger)
    {
        _logger = logger;
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
    }

    /// <summary>列出当前处于 Active 状态的输出或输入端点。</summary>
    public IReadOnlyList<AudioEndpoint> ListActive(bool render)
    {
        var result = new List<AudioEndpoint>();
        var flow = render ? EDataFlow.eRender : EDataFlow.eCapture;
        int hr = _enumerator.EnumAudioEndpoints(flow, DeviceState.Active, out var collection);
        if (hr != 0 || collection is null)
        {
            _logger.Warn($"枚举{(render ? "输出" : "输入")}设备失败，hr=0x{hr:X8}");
            return result;
        }

        if (collection.GetCount(out int count) != 0) return result;
        for (int i = 0; i < count; i++)
        {
            if (collection.Item(i, out var device) != 0 || device is null) continue;

            var id = ReadId(device);
            if (string.IsNullOrEmpty(id)) continue;

            var name = ReadFriendlyName(device);
            result.Add(new AudioEndpoint
            {
                Id = id,
                Name = string.IsNullOrEmpty(name) ? id : name,
                IsRender = render
            });
        }
        return result;
    }

    /// <summary>回读某个方向、某个角色当前的默认设备 ID，用于切换后校验。</summary>
    public string? GetDefaultId(bool render, ERole role)
    {
        var flow = render ? EDataFlow.eRender : EDataFlow.eCapture;
        int hr = _enumerator.GetDefaultAudioEndpoint(flow, role, out var device);
        if (hr != 0 || device is null) return null;
        return ReadId(device);
    }

    public bool RegisterNotifications(IMMNotificationClient client)
    {
        int hr = _enumerator.RegisterEndpointNotificationCallback(client);
        if (hr == 0)
        {
            _registeredClient = client;
            return true;
        }
        _logger.Warn($"注册设备变化通知失败，hr=0x{hr:X8}（自动重新应用功能可能不生效）");
        return false;
    }

    /// <summary>
    /// 解析设备引用。ID 优先；ID 失效时按名称兜底并给出明确告警，绝不静默失败。
    /// </summary>
    public ResolveResult Resolve(DeviceRef reference, bool render, string label)
    {
        if (reference.IsEmpty)
        {
            return new ResolveResult(false, "", $"{label}尚未绑定设备，请在「设置」里选择。");
        }

        var devices = ListActive(render).ToList();
        var id = reference.Id.Trim();
        var name = reference.Name.Trim();

        if (!string.IsNullOrEmpty(id))
        {
            var exact = devices.FirstOrDefault(d =>
                string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return new ResolveResult(true, exact.Id, "");

            // 驱动重装 / 换 USB 口后 ID 会变，此时靠名称兜底，并提醒重新绑定。
            if (string.IsNullOrEmpty(name))
            {
                return new ResolveResult(false, "",
                    $"{label}的设备 ID 已失效，且配置里没有记录名称，无法兜底匹配。请在「设置」里重新绑定。");
            }

            var byName = MatchByName(devices, name);
            if (byName is not null)
            {
                return new ResolveResult(true, byName.Id,
                    $"{label}的设备 ID 已失效，已按名称兜底匹配到「{byName.Name}」，建议在设置里重新绑定。");
            }
            return new ResolveResult(false, "",
                $"{label}的设备 ID 已失效，名称「{name}」在当前活动设备中也找不到。");
        }

        if (string.IsNullOrEmpty(name))
        {
            return new ResolveResult(false, "", $"{label}未填写设备名称或 ID。");
        }

        var matched = MatchByName(devices, name);
        if (matched is null)
        {
            return new ResolveResult(false, "",
                $"{label}找不到名称包含「{name}」的活动设备。");
        }
        if (!string.Equals(matched.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return new ResolveResult(true, matched.Id,
                $"{label}按名称模糊匹配到「{matched.Name}」，若不是目标设备请在设置里改用精确选择。");
        }
        return new ResolveResult(true, matched.Id, "");
    }

    /// <summary>先精确名称，再包含匹配；多个候选时取第一个并给出告警。</summary>
    private static AudioEndpoint? MatchByName(IReadOnlyList<AudioEndpoint> devices, string name)
    {
        var exact = devices.FirstOrDefault(d =>
            string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var fuzzy = devices.Where(d =>
            d.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(d.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        return fuzzy.FirstOrDefault();
    }

    private static string ReadId(IMMDevice device)
    {
        if (device.GetId(out var ptr) != 0 || ptr == IntPtr.Zero) return "";
        try
        {
            return Marshal.PtrToStringUni(ptr) ?? "";
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    private static string ReadFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0 || store is null) return "";

        var key = PropertyKeys.DeviceFriendlyName;
        if (store.GetValue(ref key, out var value) != 0) return "";
        try
        {
            if (value.ValueType == VT_LPWSTR && value.Pointer != IntPtr.Zero)
            {
                return Marshal.PtrToStringUni(value.Pointer) ?? "";
            }
            return "";
        }
        finally
        {
            Native.PropVariantClear(ref value);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registeredClient is not null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_registeredClient); }
            catch (Exception ex) { _logger.Warn($"注销设备通知失败：{ex.Message}"); }
        }
    }
}
