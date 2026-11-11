using System.Windows.Forms;

namespace AudioPilot.Models;

/// <summary>
/// 热键「主键」名称的统一转换。低级钩子拿到的是虚拟键码，
/// 设置窗口拿到的是 WinForms 的 Keys，两边必须转成同一套字符串才能匹配。
/// 支持：主键盘 0-9 / A-Z、F1-F12、小键盘 Num0-Num9。
/// </summary>
internal static class HotkeyKeys
{
    public static string? FromVirtualKey(int vk)
    {
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x6F);
        if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);
        return null;
    }

    public static string? FromWinFormsKeys(Keys key)
    {
        if (key is >= Keys.F1 and <= Keys.F12)
        {
            return "F" + ((int)key - (int)Keys.F1 + 1);
        }
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
        {
            return "Num" + ((int)key - (int)Keys.NumPad0);
        }

        // 去掉 KeyEventArgs 附带的修饰键标志位，只保留虚拟键码。
        int vk = (int)key & 0xFF;
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        return null;
    }

    /// <summary>修饰键本身不能当作热键的主键。</summary>
    public static bool IsModifierVirtualKey(int vk)
    {
        return (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C) || (vk >= 0xA0 && vk <= 0xA5);
    }
}
