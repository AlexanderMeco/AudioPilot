using System.Windows.Forms;
using AudioPilot.Interop;
using AudioPilot.Models;

namespace AudioPilot.UI;

/// <summary>
/// 热键录入框：按住修饰键再敲主键即可录入，例如「右Alt + 0」。
/// 左右 Alt 通过 GetAsyncKeyState(VK_RMENU / VK_LMENU) 区分。
/// </summary>
internal sealed class HotkeyInputBox : TextBox
{
    public HotkeyBinding? Binding { get; private set; }

    public HotkeyInputBox()
    {
        ReadOnly = true;
        TabStop = false;
        Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
    }

    public void SetBinding(HotkeyBinding? binding)
    {
        Binding = binding?.Clone();
        Text = Binding?.Display ?? "";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 阻止 ReadOnly TextBox 的系统按键行为与提示音。
        e.SuppressKeyPress = true;
        e.Handled = true;

        int vk = (int)e.KeyCode & 0xFF;
        if (HotkeyKeys.IsModifierVirtualKey(vk)) return;

        var key = HotkeyKeys.FromWinFormsKeys(e.KeyCode);
        if (key is null) return;

        bool rightAlt = Native.IsKeyDown(Native.VK_RMENU);
        bool leftAlt = Native.IsKeyDown(Native.VK_LMENU);
        bool anyAlt = e.Alt || rightAlt || leftAlt;

        var flags = HotkeyModifiers.None;
        if (e.Control || Native.IsKeyDown(Native.VK_CONTROL)) flags |= HotkeyModifiers.Ctrl;
        if (e.Shift || Native.IsKeyDown(Native.VK_SHIFT)) flags |= HotkeyModifiers.Shift;
        if (Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN)) flags |= HotkeyModifiers.Win;

        if (rightAlt) flags |= HotkeyModifiers.RightAlt;
        else if (leftAlt) flags |= HotkeyModifiers.LeftAlt;
        else if (anyAlt) flags |= HotkeyModifiers.Alt;

        Binding = new HotkeyBinding
        {
            Modifiers = HotkeyNames.ToConfigString(flags),
            Key = key
        };
        Text = Binding.Display;
    }
}
