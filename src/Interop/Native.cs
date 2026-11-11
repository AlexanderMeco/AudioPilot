using System.Runtime.InteropServices;

namespace AudioPilot.Interop;

/// <summary>
/// 本程序用到的 Win32 常量、结构体与 P/Invoke 声明。
///
/// 设计说明：RegisterHotKey 的 MOD_ALT 不区分左右 Alt，无法表达「右 Alt + 数字」，
/// 因此全局热键改用 WH_KEYBOARD_LL 低级键盘钩子，通过 VK_RMENU 识别右 Alt。
/// </summary>
internal static class Native
{
    public const int WH_KEYBOARD_LL = 13;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104; // 按住 Alt 时产生的是 SYSKEY 消息
    public const int WM_SYSKEYUP = 0x0105;

    public const uint LLKHF_EXTENDEDKEY = 0x01;

    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;   // 不区分左右的 Alt
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;  // 左 Alt
    public const int VK_RMENU = 0xA5;  // 右 Alt

    /// <summary>低级键盘钩子回调携带的按键信息。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    /// <summary>释放 COM 返回的 PROPVARIANT 内部内存，避免常驻进程内存泄漏。</summary>
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant pvar);

    /// <summary>判断某个虚拟键当前是否按下（设置窗口录入热键时用来区分左右 Alt）。</summary>
    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}
