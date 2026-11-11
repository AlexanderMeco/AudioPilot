using AudioGroup.Models;
using AudioGroup.UI;

namespace AudioGroup.Core;

/// <summary>
/// 切换流程的编排层：调用主内核、记录当前分组、发反馈、处理设备变化后的自动重应用。
/// 所有对外方法都在 UI 线程执行（由 UiDispatcher 保证）。
/// </summary>
internal sealed class SwitchService : IDisposable
{
    private const int SelfApplySuppressionMs = 3000;

    private readonly ConfigStore _store;
    private readonly DeviceCatalog _catalog;
    private readonly DeviceSwitcher _switcher;
    private readonly Feedback _feedback;
    private readonly Logger _logger;
    private readonly UiDispatcher _ui;
    private readonly DeviceNotifier _notifier;
    private readonly System.Windows.Forms.Timer _reapplyTimer;

    private AppConfig _config;
    private DateTime _lastSelfApplyUtc = DateTime.MinValue;

    /// <summary>当前分组或配置变化，托盘据此刷新菜单。</summary>
    public event Action? StateChanged;

    /// <summary>需要用户注意的严重问题，托盘据此弹出气泡。</summary>
    public event Action<string>? Alert;

    public AppConfig Config => _config;

    public SwitchService(
        AppConfig config,
        ConfigStore store,
        DeviceCatalog catalog,
        DeviceSwitcher switcher,
        Feedback feedback,
        Logger logger,
        UiDispatcher ui)
    {
        _config = config;
        _store = store;
        _catalog = catalog;
        _switcher = switcher;
        _feedback = feedback;
        _logger = logger;
        _ui = ui;

        _reapplyTimer = new System.Windows.Forms.Timer();
        _reapplyTimer.Tick += (_, _) =>
        {
            _reapplyTimer.Stop();
            DoReapply();
        };

        // COM 回调可能来自任意线程，先投递到 UI 线程再做防抖。
        _notifier = new DeviceNotifier(() => _ui.Post(ScheduleReapply));
    }

    public void Start()
    {
        _catalog.RegisterNotifications(_notifier);
        ApplyAutoStart();
        _logger.Info("AudioGroup 已启动。");
    }

    /// <summary>应用一个分组。silent = true 用于后台自动重应用，不弹提示、不响提示音。</summary>
    public void ApplyProfile(Profile profile, bool silent = false)
    {
        try
        {
            var result = _switcher.Apply(profile, _config);
            _lastSelfApplyUtc = DateTime.UtcNow;

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
            _logger.Error($"切换分组「{profile.Name}」时发生异常：{ex}");
            _feedback.Warning("切换异常，详见日志");
            Alert?.Invoke($"切换异常：{ex.Message}");
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
    /// 设备插拔 / 默认设备被 Windows 改动时触发，做防抖后重新应用当前分组。
    /// 抑制窗口用于忽略「我们自己切换」引起的 OnDefaultDeviceChanged，避免无限循环。
    /// </summary>
    private void ScheduleReapply()
    {
        if (!_config.ReapplyOnDeviceChange) return;
        if ((DateTime.UtcNow - _lastSelfApplyUtc).TotalMilliseconds < SelfApplySuppressionMs) return;
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
