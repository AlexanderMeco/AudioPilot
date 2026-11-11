using AudioGroup.Interop;
using AudioGroup.Models;

namespace AudioGroup.Core;

/// <summary>一次切换的结果，Messages 里同时包含告警与错误，供界面展示并写入日志。</summary>
internal sealed class SwitchResult
{
    public bool Success { get; set; } = true;
    public List<string> Messages { get; } = new();

    public string Summary => Messages.Count == 0 ? "" : string.Join("；", Messages);
}

/// <summary>
/// 切换主内核：解析设备 → 6 次 SetDefaultEndpoint → 回读校验 → 必要时降级到备用后端。
///
/// 为什么是 6 次：Windows 对输出(Render)和输入(Capture)各维护
/// eConsole / eMultimedia / eCommunications 三套默认值，漏掉任何一个都会出现
/// 「音乐换了设备、会议软件还用旧麦克风」这类问题。
/// </summary>
internal sealed class DeviceSwitcher
{
    private readonly DeviceCatalog _catalog;
    private readonly FallbackSwitcher _fallback;
    private readonly Logger _logger;

    public DeviceSwitcher(DeviceCatalog catalog, FallbackSwitcher fallback, Logger logger)
    {
        _catalog = catalog;
        _fallback = fallback;
        _logger = logger;
    }

    private readonly record struct Call(string Id, bool Render, ERole Role, string Label);

    public SwitchResult Apply(Profile profile, AppConfig config)
    {
        var result = new SwitchResult();

        // 1) 解析设备引用。通话设备留空时沿用普通输出 / 输入。
        var output = Resolve(profile.Output, render: true, "输出", result);
        var input = Resolve(profile.Input, render: false, "输入", result);
        var outputComm = profile.OutputComm.IsEmpty
            ? output
            : Resolve(profile.OutputComm, render: true, "通话输出", result);
        var inputComm = profile.InputComm.IsEmpty
            ? input
            : Resolve(profile.InputComm, render: false, "通话输入", result);

        if (output is null || input is null || outputComm is null || inputComm is null)
        {
            result.Success = false;
            return result;
        }

        var calls = new[]
        {
            new Call(output, true, ERole.eConsole, "输出·控制台"),
            new Call(output, true, ERole.eMultimedia, "输出·多媒体"),
            new Call(outputComm, true, ERole.eCommunications, "输出·通信"),
            new Call(input, false, ERole.eConsole, "输入·控制台"),
            new Call(input, false, ERole.eMultimedia, "输入·多媒体"),
            new Call(inputComm, false, ERole.eCommunications, "输入·通信")
        };

        // 2) 创建 PolicyConfigClient，内部自动做三版本 QI 回退。
        var policy = new PolicyConfigClient();
        if (!policy.IsAvailable)
        {
            result.Success = false;
            result.Messages.Add("无法获取 IPolicyConfig 接口（当前 Windows 版本可能已变更）。");
            _logger.Error("PolicyConfig COM 对象创建成功但三个 IID 均 QueryInterface 失败。");
            return result;
        }
        _logger.Info($"切换分组「{profile.Name}」，命中接口 {policy.InterfaceName}");

        // 3) 逐项设置；单项失败时按配置尝试备用后端。
        foreach (var call in calls)
        {
            int hr = policy.SetDefaultEndpoint(call.Id, call.Role);
            if (hr == 0) continue;

            var reason = $"{call.Label} 设置失败，hr=0x{hr:X8}";
            if (config.Fallback.Enabled)
            {
                _logger.Warn($"{reason}，尝试备用后端。");
                var target = _catalog.ListActive(call.Render)
                    .FirstOrDefault(d => string.Equals(d.Id, call.Id, StringComparison.OrdinalIgnoreCase));
                var nameOrId = target?.Name ?? call.Id;
                if (_fallback.TrySet(config.Fallback, nameOrId, call.Role, call.Render))
                {
                    result.Messages.Add($"{call.Label} 已由备用后端完成。");
                    continue;
                }
                reason += "，备用后端也未成功";
            }
            result.Messages.Add(reason);
            result.Success = false;
            _logger.Error(reason);
        }

        // 4) 回读校验：确认系统真的把每个角色指向了目标设备。
        foreach (var call in calls)
        {
            var actual = _catalog.GetDefaultId(call.Render, call.Role);
            if (string.Equals(actual, call.Id, StringComparison.OrdinalIgnoreCase)) continue;

            var message = $"{call.Label} 回读校验不一致（期望 {ShortId(call.Id)}，实际 {ShortId(actual)}）";
            result.Messages.Add(message);
            result.Success = false;
            _logger.Warn(message);
        }

        if (result.Success)
        {
            _logger.Info($"分组「{profile.Name}」切换完成并通过校验。");
        }
        return result;
    }

    private string? Resolve(DeviceRef reference, bool render, string label, SwitchResult result)
    {
        var resolved = _catalog.Resolve(reference, render, label);
        if (!string.IsNullOrEmpty(resolved.Message))
        {
            result.Messages.Add(resolved.Message);
            if (resolved.Success) _logger.Warn(resolved.Message);
            else _logger.Error(resolved.Message);
        }
        return resolved.Success ? resolved.Id : null;
    }

    /// <summary>日志里只保留设备 ID 的尾段，避免整屏都是 GUID。</summary>
    private static string ShortId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "(无)";
        return id.Length <= 24 ? id : "..." + id[^24..];
    }
}
