using System.Runtime.InteropServices;

namespace InputBlocker;

// ============================================================
// 文件：NativeMethods.cs
// 功能：封装所有 Windows API 的 P/Invoke 声明、常量、结构体
//       供 HookManager 和 MainForm 调用
// 注意：所有 DllImport 均针对 user32.dll / kernel32.dll，
//       兼容 Windows 7 及以上系统
// ============================================================

/// <summary>
/// Windows 原生 API 封装类（静态）
/// 包含钩子安装/卸载、光标控制、按键状态检测等全部 P/Invoke 声明
/// </summary>
internal static class NativeMethods
{
    // ======================== 钩子类型常量 ========================

    /// <summary>键盘低层钩子（全局生效，应用层）</summary>
    public const int WH_KEYBOARD_LL = 13;

    /// <summary>鼠标低层钩子（全局生效，应用层）</summary>
    public const int WH_MOUSE_LL = 14;

    /// <summary>钩子回调有效标志</summary>
    public const int HC_ACTION = 0;

    // ======================== 键盘消息常量 ========================

    public const int WM_KEYDOWN    = 0x0100;  // 普通键按下
    public const int WM_KEYUP      = 0x0101;  // 普通键释放
    public const int WM_SYSKEYDOWN = 0x0104;  // 系统键（Alt组合）按下
    public const int WM_SYSKEYUP   = 0x0105;  // 系统键（Alt组合）释放

    // ======================== 鼠标消息常量 ========================

    public const int WM_MOUSEMOVE    = 0x0200;  // 鼠标移动
    public const int WM_LBUTTONDOWN  = 0x0201;  // 左键按下
    public const int WM_LBUTTONUP    = 0x0202;  // 左键释放
    public const int WM_RBUTTONDOWN  = 0x0204;  // 右键按下
    public const int WM_RBUTTONUP    = 0x0205;  // 右键释放
    public const int WM_MBUTTONDOWN  = 0x0207;  // 中键按下
    public const int WM_MBUTTONUP    = 0x0208;  // 中键释放
    public const int WM_MOUSEWHEEL   = 0x020A;  // 滚轮滚动
    public const int WM_XBUTTONDOWN  = 0x020B;  // 扩展键（侧键）按下
    public const int WM_XBUTTONUP    = 0x020C;  // 扩展键（侧键）释放

    // ======================== 虚拟键码常量 ========================

    public const int VK_CONTROL = 0x11;  // Ctrl 键
    public const int VK_MENU    = 0x12;  // Alt 键
    public const int VK_SHIFT   = 0x10;  // Shift 键
    public const int VK_LWIN    = 0x5B;  // 左 Win 键
    public const int VK_RWIN    = 0x5C;  // 右 Win 键

    // ======================== 委托类型 ========================

    /// <summary>
    /// 低层钩子回调委托
    /// 参数：nCode - 钩子动作码；wParam - 消息类型；lParam - 结构体指针
    /// 返回：非0值表示拦截消息，0/CallNextHookEx结果表示放行
    /// 注意：此委托实例必须被强引用持有，否则会被GC回收导致崩溃
    /// </summary>
    public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ======================== P/Invoke 函数声明 ========================

    /// <summary>
    /// 安装 Windows 消息钩子
    /// 参数：idHook - 钩子类型（WH_KEYBOARD_LL / WH_MOUSE_LL）
    ///       lpfn - 回调委托
    ///       hMod - 当前模块句柄（低层钩子必须传自身模块）
    ///       dwThreadId - 0 表示全局钩子
    /// 返回：钩子句柄（IntPtr.Zero 表示失败）
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelHookProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    /// <summary>
    /// 卸载已安装的钩子
    /// 参数：hhk - 由 SetWindowsHookEx 返回的钩子句柄
    /// 返回：true 成功，false 失败
    /// 注意：进程退出时系统会自动卸载该进程所有钩子，但显式卸载更安全
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    /// <summary>
    /// 将消息传递给钩子链中的下一个钩子
    /// 参数：hhk - 当前钩子句柄（可传 IntPtr.Zero）
    ///       nCode - 传递给下一个钩子的动作码
    ///       wParam / lParam - 原始消息参数
    /// 注意：如果不调用此函数，会阻断整个钩子链，影响其他程序
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    /// <summary>
    /// 获取当前进程模块句柄
    /// 参数：lpModuleName - 模块名（null 返回当前exe模块句柄）
    /// 返回：模块句柄
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>
    /// 检测指定虚拟键的当前物理状态（异步检测，非队列状态）
    /// 参数：vKey - 虚拟键码
    /// 返回：最高位为1表示当前按下，最低位为1表示上次调用后按过
    /// 注意：此函数在钩子回调中用于检测修饰键状态（Ctrl/Alt/Shift）
    /// </summary>
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// 显示或隐藏鼠标光标（内部计数器机制）
    /// 参数：bShow - true 增加显示计数，false 减少显示计数
    /// 返回：调用前的显示计数器值
    /// 注意：必须在停止屏蔽时调用足够多次 ShowCursor(true) 恢复光标
    /// </summary>
    [DllImport("user32.dll")]
    public static extern int ShowCursor(bool bShow);

    /// <summary>
    /// 将鼠标光标限制在指定矩形区域内
    /// 参数：ref RECT - 限制区域（传入 IntPtr.Zero 或 null 则解除限制）
    /// 返回：true 成功
    /// 注意：此函数只限制光标物理移动，不拦截点击和滚轮
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool ClipCursor(ref RECT lpRect);

    /// <summary>
    /// 解除所有光标区域限制（重载版本）
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool ClipCursor(IntPtr lpRect);

    /// <summary>
    /// 获取当前鼠标光标屏幕坐标
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>
    /// 根据屏幕坐标获取该位置所属窗口的句柄
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT Point);

    // ======================== 结构体定义 ========================

    /// <summary>屏幕坐标点</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>矩形区域（用于 ClipCursor）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// 键盘钩子回调中的按键信息结构体
    /// vkCode - 虚拟键码（如 Keys.A = 0x41）
    /// scanCode - 硬件扫描码
    /// flags - 标志位（bit7=1表示按键释放，bit0=1表示扩展键）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>
    /// 鼠标钩子回调中的鼠标信息结构体
    /// pt - 屏幕坐标
    /// mouseData - 滚轮/扩展键额外数据
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}