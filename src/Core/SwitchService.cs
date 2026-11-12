using AudioPilot.Models;
using AudioPilot.UI;

namespace AudioPilot.Core;

/// <summary>
/// 切换流程的编排层：把耗时的 COM 切换投递到 SwitchWorker（独立 STA 线程），
/// 结果回到 UI 线程后再做界面反馈、配置保存与事件通知。
/// 所有对外方法仍在 UI 线程调用，界面代码不需要关心线程细节。
/// </summary>
internal sealed class SwitchService : IDisposable
{
    private const int SelfApplySuppressionMs = 3000;

    private readonly ConfigStore _store;
    private readonly DeviceCatalog _catalog;  // UI 线程：设置窗口枚举 + 设备通知注册
    private readonly SwitchWorker _worker;    // 工作线程：设备目录缓存 + 切换内核
    private readonly Feedback _feedback;
    private readonly Logger _logger;
    private readonly UiDispatcher _ui;
    private readonly DeviceNotifier _notifier;
    private readonly System.Windows.Forms.Timer _reapplyTimer;

    private volatile AppConfig _config;
    private long _lastSelfApplyTicks;

    /// <summary>当前分组或配置变化，托盘据此刷新菜单。</summary>
    public event Action? StateChanged;

    /// <summary>需要用户注意的严重问题，托盘据此弹出气泡。</summary>
    public event Action<string>? Alert;

    public AppConfig Config => _config;

    public SwitchService(
        AppConfig config,
        ConfigStore store,
        DeviceCatalog catalog,
        SwitchWorker worker,
        Feedback feedback,
        Logger logger,
        UiDispatcher ui)
    {
        _config = config;
        _store = store;
        _catalog = catalog;
        _worker = worker;
        _feedback = feedback;
        _logger = logger;
        _ui = ui;

        _reapplyTimer = new System.Windows.Forms.Timer();
        _reapplyTimer.Tick += (_, _) =>
        {
            _reapplyTimer.Stop();
            DoReapply();
        };

        // COM 回调可能来自任意线程：先回 UI 线程，再让两份设备目录缓存一起失效。
        _notifier = new DeviceNotifier(() => _ui.Post(() =>
        {
            _catalog.InvalidateCache();
            _worker.InvalidateCache();
            ScheduleReapply();
        }));
    }

    public void Start()
    {
        _catalog.RegisterNotifications(_notifier);
        ApplyAutoStart();
        _logger.Info("AudioPilot 已启动。");
    }

    /// <summary>
    /// 应用一个分组。切换本体在工作线程执行，避免阻塞 UI 线程的消息泵 ——
    /// 低级键盘钩子的回调同样依赖这个消息泵，被长时间占用会被系统摘钩。
    /// silent = true 用于后台自动重应用，不弹提示、不响提示音。
    /// </summary>
    public void ApplyProfile(Profile profile, bool silent = false)
    {
        var configSnapshot = _config;
        _worker.Post((_, switcher) =>
        {
            SwitchResult result;
            try
            {
                result = switcher.Apply(profile, configSnapshot);
            }
            catch (Exception ex)
            {
                _logger.Error($"切换分组「{profile.Name}」时发生异常：{ex}");
                result = new SwitchResult { Success = false };
                result.Messages.Add(ex.Message);
            }

            Interlocked.Exchange(ref _lastSelfApplyTicks, DateTime.UtcNow.Ticks);
            _ui.Post(() => FinishApply(profile, result, silent));
        });
    }

    /// <summary>切换结果回到 UI 线程后：更新状态、发反馈、保存配置、通知托盘刷新。</summary>
    private void FinishApply(Profile profile, SwitchResult result, bool silent)
    {
        try
        {
            if (result.Success)
            {
                _config.ActiveProfileName = profile.Name;
                SaveConfig();

                if (!silent)
                {
                    var text = result.Messages.Count == 0
                        ? $"已切换：{profile.Name}"
                        : $"已切换：{profile.Name}（有告警，详见日志）";
                    _feedback.Success(text);
                }
            }
            else
            {
                var detail = result.Messages.FirstOrDefault() ?? "未知错误";
                _feedback.Warning($"切换失败：{profile.Name}");
                Alert?.Invoke($"切换失败：{profile.Name}\n{detail}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"更新切换结果时发生异常：{ex}");
        }
        finally
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>循环切换到下一组。</summary>
    public void CycleNext()
    {
        if (_config.Profiles.Count == 0)
        {
            _feedback.Warning("当前没有可用分组");
            return;
        }

        int index = _config.Profiles.FindIndex(p =>
            string.Equals(p.Name, _config.ActiveProfileName, StringComparison.Ordinal));
        int next = (index + 1) % _config.Profiles.Count;
        ApplyProfile(_config.Profiles[next]);
    }

    /// <summary>设置窗口保存后调用：替换配置并同步开机自启。</summary>
    public void ReloadConfig(AppConfig config)
    {
        _config = config;
        ApplyAutoStart();
        SaveConfig();
        _logger.Info($"配置已更新，共 {config.Profiles.Count} 个分组。");
        StateChanged?.Invoke();
    }

    private void SaveConfig()
    {
        try
        {
            _store.Save(_config);
        }
        catch (Exception ex)
        {
            _logger.Warn($"保存配置失败：{ex.Message}");
        }
    }

    private void ApplyAutoStart()
    {
        try
        {
            AutoStart.SetEnabled(_config.AutoStart);
        }
        catch (Exception ex)
        {
            _logger.Warn($"设置开机自启失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 设备插拔 / 默认设备被 Windows 改动时触发，防抖后重新应用当前分组。
    /// 抑制窗口用于忽略「我们自己切换」引起的 OnDefaultDeviceChanged，避免无限循环。
    /// </summary>
    private void ScheduleReapply()
    {
        if (!_config.ReapplyOnDeviceChange) return;

        var last = new DateTime(Interlocked.Read(ref _lastSelfApplyTicks), DateTimeKind.Utc);
        if ((DateTime.UtcNow - last).TotalMilliseconds < SelfApplySuppressionMs) return;
        if (string.IsNullOrEmpty(_config.ActiveProfileName)) return;

        _reapplyTimer.Interval = Math.Clamp(_config.ReapplyDebounceMs, 500, 30000);
        _reapplyTimer.Stop();
        _reapplyTimer.Start();
    }

    private void DoReapply()
    {
        if (!_config.ReapplyOnDeviceChange) return;
        var profile = _config.Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, _config.ActiveProfileName, StringComparison.Ordinal));
        if (profile is null) return;

        _logger.Info($"检测到设备变化，自动重新应用分组「{profile.Name}」。");
        ApplyProfile(profile, silent: true);
    }

    public void Dispose()
    {
        _reapplyTimer.Stop();
        _reapplyTimer.Dispose();
    }
}
