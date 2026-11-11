using System.Text.Json.Serialization;

namespace AudioPilot.Models;

/// <summary>
/// 热键修饰键。RightAlt / LeftAlt 由低级键盘钩子单独识别，
/// Alt 表示「任意一侧 Alt」，Ctrl / Shift / Win 不区分左右。
/// </summary>
[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
    RightAlt = 16,
    LeftAlt = 32
}

internal static class HotkeyNames
{
    private static readonly (HotkeyModifiers Flag, string Token, string Text)[] All =
    {
        (HotkeyModifiers.Ctrl, "Ctrl", "Ctrl"),
        (HotkeyModifiers.Shift, "Shift", "Shift"),
        (HotkeyModifiers.Win, "Win", "Win"),
        (HotkeyModifiers.Alt, "Alt", "Alt"),
        (HotkeyModifiers.RightAlt, "RightAlt", "右Alt"),
        (HotkeyModifiers.LeftAlt, "LeftAlt", "左Alt")
    };

    /// <summary>把 "RightAlt,Ctrl" 这样的配置字符串解析成标志位。</summary>
    public static HotkeyModifiers Parse(string? text)
    {
        var result = HotkeyModifiers.None;
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var item in All)
            {
                if (string.Equals(part, item.Token, StringComparison.OrdinalIgnoreCase))
                {
                    result |= item.Flag;
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>把标志位还原成配置字符串，顺序固定，保证 JSON 稳定易读。</summary>
    public static string ToConfigString(HotkeyModifiers flags)
    {
        var parts = new List<string>();
        foreach (var item in All)
        {
            if (flags.HasFlag(item.Flag)) parts.Add(item.Token);
        }
        return parts.Count == 0 ? "None" : string.Join(",", parts);
    }

    /// <summary>转成界面显示的中文文本，例如「右Alt + 0」。</summary>
    public static string ToDisplayText(HotkeyModifiers flags, string key)
    {
        var parts = new List<string>();
        foreach (var item in All)
        {
            if (flags.HasFlag(item.Flag)) parts.Add(item.Text);
        }
        parts.Add(key);
        return string.Join(" + ", parts);
    }
}

/// <summary>一个热键组合。Modifiers 以字符串存 JSON，便于手工编辑。</summary>
internal sealed class HotkeyBinding
{
    public string Modifiers { get; set; } = "RightAlt";
    public string Key { get; set; } = "0";

    [JsonIgnore]
    public HotkeyModifiers ModifierFlags => HotkeyNames.Parse(Modifiers);

    [JsonIgnore]
    public string Display => HotkeyNames.ToDisplayText(ModifierFlags, Key);

    public HotkeyBinding Clone() => new() { Modifiers = Modifiers, Key = Key };
}

/// <summary>
/// 对一个音频端点的引用。Id 为 WASAPI 端点 ID（精确、稳定），
/// Name 为友好名称（驱动重装后 ID 变化时用于模糊兜底）。
/// </summary>
internal sealed class DeviceRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Id) && string.IsNullOrWhiteSpace(Name);

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Name)
        ? (string.IsNullOrWhiteSpace(Id) ? "（未设置）" : Id)
        : Name;

    public DeviceRef Clone() => new() { Id = Id, Name = Name };
}

/// <summary>
/// 一个场景分组：一台输出 + 一台输入，外加可选的「通话角色专用设备」。
/// OutputComm / InputComm 留空时，eCommunications 沿用 Output / Input；
/// 蓝牙耳机通常需要把通话指向 Hands-Free 端点，此时单独指定。
/// </summary>
internal sealed class Profile
{
    public string Name { get; set; } = "新分组";
    public HotkeyBinding Hotkey { get; set; } = new();
    public DeviceRef Output { get; set; } = new();
    public DeviceRef Input { get; set; } = new();
    public DeviceRef OutputComm { get; set; } = new();
    public DeviceRef InputComm { get; set; } = new();

    public Profile Clone() => new()
    {
        Name = Name,
        Hotkey = Hotkey.Clone(),
        Output = Output.Clone(),
        Input = Input.Clone(),
        OutputComm = OutputComm.Clone(),
        InputComm = InputComm.Clone()
    };
}

/// <summary>
/// 备用切换后端，默认关闭。仅在主内核 SetDefaultEndpoint 失败时启用。
/// kind = soundvolumeview（推荐）或 audiodevicecmdlets（PowerShell 模块）。
/// </summary>
internal sealed class FallbackConfig
{
    public bool Enabled { get; set; }
    public string Kind { get; set; } = "soundvolumeview";
    public string SoundVolumeViewPath { get; set; } = "";
    public string PowerShellPath { get; set; } = "powershell.exe";
}

/// <summary>程序全部配置，对应 exe 同目录的 profiles.json。</summary>
internal sealed class AppConfig
{
    public int Version { get; set; } = 1;

    /// <summary>开机自启（HKCU Run，免管理员权限）。</summary>
    public bool AutoStart { get; set; }

    /// <summary>切换成功后播放提示音，顺便验证新的输出设备是否生效。</summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>右下角无焦点提示条。</summary>
    public bool ShowOsd { get; set; } = true;

    /// <summary>设备插拔后自动重新应用当前分组，防止 Windows 抢默认设备。</summary>
    public bool ReapplyOnDeviceChange { get; set; } = true;

    public int ReapplyDebounceMs { get; set; } = 2000;

    /// <summary>当前选中的分组名，重启后仍保持。</summary>
    public string? ActiveProfileName { get; set; }

    /// <summary>循环切换到下一组的热键，默认右Alt + 9。</summary>
    public HotkeyBinding CycleHotkey { get; set; } = new() { Modifiers = "RightAlt", Key = "9" };

    public FallbackConfig Fallback { get; set; } = new();

    public List<Profile> Profiles { get; set; } = new();

    /// <summary>首次运行生成 3 个空分组，热键从右Alt + 0 开始。</summary>
    public static AppConfig CreateDefault()
    {
        var config = new AppConfig();
        string[] names = { "会议", "影音", "游戏" };
        for (int i = 0; i < names.Length; i++)
        {
            config.Profiles.Add(new Profile
            {
                Name = names[i],
                Hotkey = new HotkeyBinding { Modifiers = "RightAlt", Key = i.ToString() }
            });
        }
        return config;
    }
}
