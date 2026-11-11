using AudioPilot.Interop;

namespace AudioPilot.Core;

/// <summary>
/// IMMNotificationClient 的托管实现。COM 回调可能来自非 UI 线程，
/// 因此这里只投递一个「设备有变化」的信号，实际处理由 UiDispatcher 转交 UI 线程。
/// 所有方法返回 0（S_OK）。
/// </summary>
internal sealed class DeviceNotifier : IMMNotificationClient
{
    private readonly Action _onChanged;

    public DeviceNotifier(Action onChanged) => _onChanged = onChanged;

    public int OnDeviceStateChanged(string deviceId, int newState)
    {
        Notify();
        return 0;
    }

    public int OnDeviceAdded(string deviceId)
    {
        Notify();
        return 0;
    }

    public int OnDeviceRemoved(string deviceId)
    {
        Notify();
        return 0;
    }

    public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
    {
        Notify();
        return 0;
    }

    public int OnPropertyValueChanged(string deviceId, PropertyKey key)
    {
        // 属性变化（例如音量）不改变默认设备，忽略以避免不必要的重应用。
        return 0;
    }

    private void Notify() => _onChanged();
}
