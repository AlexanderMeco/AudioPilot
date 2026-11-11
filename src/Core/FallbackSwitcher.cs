using System.Diagnostics;
using AudioPilot.Interop;
using AudioPilot.Models;

namespace AudioPilot.Core;

/// <summary>
/// 备用切换后端：仅当主内核 SetDefaultEndpoint 返回失败 HRESULT 时调用，默认关闭。
/// 存在的意义是给「未公开的 COM 接口未来被改掉」留一条退路。
///
/// 两种实现：
///   soundvolumeview      —— NirSound 的 SoundVolumeView.exe /SetDefault，能覆盖全部 3 个角色（推荐）。
///   audiodevicecmdlets   —— PowerShell 模块 Set-AudioDevice，只能设 eMultimedia + eCommunications，
///                          无法设置 eConsole（这是该项目源码的固有行为），仅作最后兜底。
/// </summary>
internal sealed class FallbackSwitcher
{
    private readonly Logger _logger;

    public FallbackSwitcher(Logger logger) => _logger = logger;

    public bool TrySet(FallbackConfig config, string idOrName, ERole role, bool render)
    {
        string exe;
        string arguments;

        if (string.Equals(config.Kind, "audiodevicecmdlets", StringComparison.OrdinalIgnoreCase))
        {
            exe = string.IsNullOrWhiteSpace(config.PowerShellPath)
                ? "powershell.exe"
                : config.PowerShellPath;
            arguments = "-NoProfile -NonInteractive -Command \"Import-Module AudioDeviceCmdlets; " +
                        $"Set-AudioDevice -ID '{idOrName}'\"";
        }
        else
        {
            exe = config.SoundVolumeViewPath.Trim();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                _logger.Warn("备用后端已启用，但 SoundVolumeView.exe 路径无效，跳过降级。");
                return false;
            }
            arguments = $"/SetDefault \"{idOrName}\" {(int)role}";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return false;
            if (!process.WaitForExit(5000)) return false;
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.Error($"备用后端执行失败：{ex.Message}");
            return false;
        }
    }
}
