using System.Collections.Concurrent;

namespace AudioGroup.Core;

/// <summary>
/// 把低级键盘钩子回调、COM 设备通知回调里的动作排队，交给 UI 线程串行执行。
/// 这样钩子回调里不做耗时的 COM 调用（回调卡太久会被系统自动摘钩），
/// 同时保证所有 COM 操作都在 UI 这个 STA 线程上完成。
/// </summary>
internal sealed class UiDispatcher : IDisposable
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Logger _logger;

    public UiDispatcher(Logger logger)
    {
        _logger = logger;
        _timer = new System.Windows.Forms.Timer { Interval = 25 };
        _timer.Tick += (_, _) => Drain();
        _timer.Start();
    }

    /// <summary>线程安全：可从任意线程调用。</summary>
    public void Post(Action action) => _queue.Enqueue(action);

    private void Drain()
    {
        while (_queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.Error($"UI 队列中的操作异常：{ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
