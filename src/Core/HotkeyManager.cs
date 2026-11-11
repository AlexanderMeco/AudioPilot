using System.Runtime.InteropServices;
using AudioGroup.Interop;
using AudioGroup.Models;

namespace AudioGroup.Core;

/// <summary>
/// 全局热键管理。使用 WH_KEYBOARD_LL 低级键盘钩子而不是 RegisterHotKey，
/// 原因是 RegisterHotKey 的 MOD_ALT 无法区分左右 Alt，而需求是「右 Alt + 数字」。
///
/// 钩子回调里只做状态判断 + 入队，真正的切换由 UiDispatcher 投递到 UI 线程执行，
/// 避免钩子线程被 COM 调用阻塞（Windows 会摘掉响应过慢的低级钩子）。
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly UiDispatcher _ui;
    private readonly Logger _logger;

    /// <summary>参数：(命中的分组, 是否为循环切换热键)。分组为 null 表示循环切换。</summary>
    private readonly Action<Profile?, bool> _onMatched;

    // 必须作为字段持有，防止委托被 GC 回收导致钩子崩溃。
    private Native.LowLevelKeyboardProc? _hookProc;
    private IntPtr _hookHandle = IntPtr.Zero;

    private List<(HotkeyBinding Binding, Profile Profile)> _bindings = new();
    private HotkeyBinding _cycle = new();

    // 当前按下的修饰键状态。
    private bool _rightAlt;
    private bool _leftAlt;
    private bool _ctrl;
    private bool _shift;
    private bool _win;

    public HotkeyManager(UiDispatcher ui, Logger logger, Action<Profile?, bool> onMatched)
    {
        _ui = ui;
        _logger = logger;
        _onMatched = onMatched;
    }

    /// <summary>配置变化后重新装载热键表（无需重装钩子）。</summary>
    public void Rebind(AppConfig config)
    {
        _bindings = config.Profiles
            .Where(p => p.Hotkey is not null)
            .Select(p => (p.Hotkey, p))
            .ToList();
        _cycle = config.CycleHotkey ?? new HotkeyBinding { Modifiers = "RightAlt", Key = "9" };

        var duplicated = _bindings
            .GroupBy(b => $"{b.Binding.Modifiers}|{b.Binding.Key}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
        {
            _logger.Warn($"存在重复的分组热键「{duplicated.Key}」，只有第一个分组会生效。");
        }
    }

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero) return;

        _hookProc = HookProc;
        var module = Native.GetModuleHandle(null);
        _hookHandle = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _hookProc, module, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            _logger.Error($"安装全局键盘钩子失败，Win32Error={Marshal.GetLastWin32Error()}；全局热键将不可用。");
            return;
        }
        _logger.Info($"全局键盘钩子已安装，共 {_bindings.Count} 个分组热键 + 1 个循环热键。");
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)data.vkCode;

            bool isDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool isUp = message is Native.WM_KEYUP or Native.WM_SYSKEYUP;

            // 右 Alt 在不同键盘布局里可能是 VK_RMENU，也可能是带扩展标志的 VK_MENU。
            bool isRightAlt = vk == Native.VK_RMENU ||
                              (vk == Native.VK_MENU && (data.flags & Native.LLKHF_EXTENDEDKEY) != 0);
            bool isLeftAlt = vk == Native.VK_LMENU || (vk == Native.VK_MENU && !isRightAlt);

            if (isDown || isUp)
            {
                if (isRightAlt) _rightAlt = isDown;
                if (isLeftAlt) _leftAlt = isDown;
                if (vk is Native.VK_CONTROL or Native.VK_LCONTROL or Native.VK_RCONTROL) _ctrl = isDown;
                if (vk is Native.VK_SHIFT or Native.VK_LSHIFT or Native.VK_RSHIFT) _shift = isDown;
                if (vk is Native.VK_LWIN or Native.VK_RWIN) _win = isDown;
            }

            if (isDown && !HotkeyKeys.IsModifierVirtualKey(vk))
            {
                var key = HotkeyKeys.FromVirtualKey(vk);
                if (key is not null)
                {
                    // 分组热键优先于循环热键。
                    var hit = _bindings.FirstOrDefault(b =>
                        string.Equals(b.Binding.Key, key, StringComparison.OrdinalIgnoreCase) &&
                        ModifiersMatch(b.Binding.ModifierFlags));

                    if (hit.Profile is not null)
                    {
                        var target = hit.Profile;
                        _ui.Post(() => _onMatched(target, false));
                        return (IntPtr)1; // 吞掉按键，避免同时触发系统菜单等行为
                    }

                    if (string.Equals(_cycle.Key, key, StringComparison.OrdinalIgnoreCase) &&
                        ModifiersMatch(_cycle.ModifierFlags))
                    {
                        _ui.Post(() => _onMatched(null, true));
                        return (IntPtr)1;
                    }
                }
            }
        }

        return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// 修饰键精确匹配：配置里没写的修饰键若正被按下，就不触发，避免误触。
    /// Alt / RightAlt / LeftAlt 三者含义不同，分别处理。
    /// </summary>
    private bool ModifiersMatch(HotkeyModifiers modifiers)
    {
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl) != _ctrl) return false;
        if (modifiers.HasFlag(HotkeyModifiers.Shift) != _shift) return false;
        if (modifiers.HasFlag(HotkeyModifiers.Win) != _win) return false;

        bool wantAnyAlt = modifiers.HasFlag(HotkeyModifiers.Alt);
        bool wantRightAlt = modifiers.HasFlag(HotkeyModifiers.RightAlt);
        bool wantLeftAlt = modifiers.HasFlag(HotkeyModifiers.LeftAlt);

        if (wantAnyAlt) return _rightAlt || _leftAlt;
        if (wantRightAlt && wantLeftAlt) return _rightAlt && _leftAlt;
        if (wantRightAlt) return _rightAlt && !_leftAlt;
        if (wantLeftAlt) return _leftAlt && !_rightAlt;
        return !_rightAlt && !_leftAlt;
    }

    public void Dispose()
    {
        if (_hookHandle == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hookProc = null;
    }
}
