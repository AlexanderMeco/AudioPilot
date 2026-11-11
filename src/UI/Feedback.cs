using System.Runtime.InteropServices;
using AudioPilot.Models;

namespace AudioPilot.UI;

/// <summary>
/// 切换后的反馈：无焦点 OSD + 提示音。
/// 提示音走 waveOut 的默认设备，切换成功后播放等于顺手验证新输出设备是否生效。
/// 若 exe 同目录放了 switch.wav，则播放该文件，否则用系统提示音。
/// </summary>
internal sealed class Feedback
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_FILENAME = 0x00020000;
    private const uint SND_NODEFAULT = 0x0002;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    private readonly Func<AppConfig> _configProvider;
    private readonly string _appDirectory;

    public Feedback(Func<AppConfig> configProvider, string appDirectory)
    {
        _configProvider = configProvider;
        _appDirectory = appDirectory;
    }

    public void Success(string text)
    {
        ShowOsd(text);
        if (_configProvider().PlaySound) PlaySwitchSound();
    }

    public void Warning(string text) => ShowOsd("注意：" + text);

    private void ShowOsd(string text)
    {
        if (_configProvider().ShowOsd) OsdForm.ShowOsd(text);
    }

    private void PlaySwitchSound()
    {
        try
        {
            var custom = Path.Combine(_appDirectory, "switch.wav");
            if (File.Exists(custom))
            {
                PlaySound(custom, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
            }
            else
            {
                System.Media.SystemSounds.Asterisk.Play();
            }
        }
        catch
        {
            // 没有声卡 / 声音被禁用时不影响切换结果。
        }
    }
}
