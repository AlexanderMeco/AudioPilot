using System.Collections.Concurrent;

namespace AudioPilot.Core;

/// <summary>
/// 专用 STA 工作线程：所有音频 COM 操作都在这里串行执行。
///
/// 为什么要独立线程：
///   1) 低级键盘钩子的回调运行在 UI 线程的消息泵里。若 6 次 SetDefaultEndpoint、
///      设备枚举与回读校验都挤在 UI 线程，钩子回调会被饿死；超过系统的
///      LowLevelHooksTimeout 后钩子会被静默摘掉，表现为「热键突然全部失效」。
///   2) MMDeviceEnumerator / PolicyConfigClient 属于 STA 单元，不能把 UI 线程
///      创建的 COM 对象直接跨线程使用。所以这里让工作线程自己创建并持有
///      DeviceCatalog 与 DeviceSwitcher，而不是共享 UI 线程的那一份。
///
/// 串行队列同时保证快速连按热键时不会并发调用 COM 造成互相覆盖。
/// </summary>
internal sealed class SwitchWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly Logger _logger;
    private readonly FallbackSwitcher _fallback;

    // 仅在本工作线程内访问，不跨线程共享。
    private DeviceCatalog? _catalog;
    private DeviceSwitcher? _switcher;

    public SwitchWorker(Logger logger, FallbackSwitcher fallback)
    {
        _logger = logger;
        _fallback = fallback;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "AudioPilot.SwitchWorker"
        };
        // COM 互操作要求 STA 单元；SetApartmentState 仅在 Windows 上可用。
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>投递一个任务，回调拿到本线程私有的目录与切换内核。</summary>
    public void Post(Action<DeviceCatalog, DeviceSwitcher> work)
    {
        if (_queue.IsAddingCompleted) return;
        _queue.Add(() => work(_catalog!, _switcher!));
    }

    /// <summary>设备变化后让工作线程的目录缓存失效（任务必须在本线程执行）。</summary>
    public void InvalidateCache()
    {
        if (_queue.IsAddingCompleted) return;
        _queue.Add(() => _catalog?.InvalidateCache());
    }

    private void Run()
    {
        // 必须先在本线程创建 COM 对象，再开始消费队列。
        _catalog = new DeviceCatalog(_logger);
        _switcher = new DeviceSwitcher(_catalog, _fallback, _logger);

        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.Error($"切换工作线程任务异常：{ex}");
            }
        }

        _catalog.Dispose();
    }

    public void Dispose()
    {
        if (_queue.IsAddingCompleted) return;
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(3));
        _queue.Dispose();
    }
}
