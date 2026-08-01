using System.Runtime.InteropServices;

namespace InputBlocker;

// ============================================================
// 文件：HookManager.cs
// 功能：全局键鼠钩子管理器
//       - 安装/卸载 WH_KEYBOARD_LL（键盘低层钩子）
//       - 安装/卸载 WH_MOUSE_LL（鼠标低层钩子）
//       - 解锁热键检测（Ctrl+Alt+Shift+F8）
//       - 光标隐藏/区域限制辅助功能
// 注意：此类的回调委托必须保持强引用，否则 GC 回收后程序崩溃
// ============================================================

/// <summary>
/// 全局钩子管理器 - 实现 IDisposable 确保资源释放
/// 核心设计：
/// 1. 键盘钩子拦截所有按键，但内置解锁热键检测逻辑，检测到热键时设置解锁标志
/// 2. 鼠标钩子拦截所有鼠标事件（移动/点击/滚轮）
/// 3. 光标隐藏和区域限制作为辅助手段叠加使用
/// 4. 所有钩子回调必须快速返回（<1ms），否则 Windows 会跳过钩子
/// 兜底机制：
///   - 解锁热键（Ctrl+Alt+Shift+F8）始终有效
///   - 调用 StopAll() 或 Dispose() 显式卸载所有钩子
///   - 进程退出时系统自动清理（不可靠，建议显式调用）
///   - Ctrl+Alt+Delete 永远无法被拦截，用户可打开任务管理器结束进程
/// </summary>
internal sealed class HookManager : IDisposable
{
    // ======================== 私有字段 ========================

    /// <summary>键盘钩子句柄（IntPtr.Zero 表示未安装）</summary>
    private IntPtr _keyboardHookHandle = IntPtr.Zero;

    /// <summary>鼠标钩子句柄（IntPtr.Zero 表示未安装）</summary>
    private IntPtr _mouseHookHandle = IntPtr.Zero;

    /// <summary>键盘钩子回调委托（必须保持引用，防止GC回收）</summary>
    private readonly NativeMethods.LowLevelHookProc _keyboardProc;

    /// <summary>鼠标钩子回调委托（必须保持引用，防止GC回收）</summary>
    private readonly NativeMethods.LowLevelHookProc _mouseProc;

    /// <summary>主窗体句柄（用于判断鼠标是否在窗体上）</summary>
    private readonly IntPtr _mainFormHandle;

    /// <summary>解锁请求标志（volatile 保证多线程可见性）</summary>
    private volatile bool _unlockRequested;

    /// <summary>光标隐藏计数（ShowCursor 使用引用计数，需要对称调用恢复）</summary>
    private int _cursorHideCount;

    /// <summary>线程同步锁</summary>
    private readonly object _lock = new();

    /// <summary>Ctrl 键当前是否按下（钩子回调内追踪，比 GetAsyncKeyState 更可靠）</summary>
    private bool _ctrlDown;

    /// <summary>Alt 键当前是否按下</summary>
    private bool _altDown;

    /// <summary>Shift 键当前是否按下</summary>
    private bool _shiftDown;

    // ======================== 公开属性 ========================

    /// <summary>键盘是否正在被屏蔽</summary>
    public bool IsKeyboardBlocked { get; private set; }

    /// <summary>鼠标是否正在被屏蔽</summary>
    public bool IsMouseBlocked { get; private set; }

    /// <summary>光标是否已隐藏</summary>
    public bool IsCursorHidden { get; private set; }

    /// <summary>光标是否已被区域限制</summary>
    public bool IsCursorClipped { get; private set; }

    /// <summary>解锁热键是否已被触发（由外部定时器轮询，触发后重置）</summary>
    public bool UnlockRequested
    {
        get => _unlockRequested;
        set => _unlockRequested = value;
    }

    // ======================== 公开事件 ========================

    /// <summary>日志输出事件（供 MainForm 显示）</summary>
    public event Action<string>? LogMessage;

    // ======================== 解锁热键配置 ========================

    /// <summary>解锁热键的目标键（默认 F8）</summary>
    public const Keys UnlockKey = Keys.F8;

    /// <summary>
    /// 判断当前修饰键是否全部按下（基于钩子回调内追踪的状态，不依赖 GetAsyncKeyState）
    /// </summary>
    private bool CheckModifiers() => _ctrlDown && _altDown && _shiftDown;

    // ======================== 构造函数 ========================

    /// <summary>
    /// 初始化钩子管理器
    /// </summary>
    /// <param name="mainFormHandle">主窗体句柄，用于鼠标钩子判断是否越过窗体</param>
    public HookManager(IntPtr mainFormHandle)
    {
        _mainFormHandle = mainFormHandle;
        // 将回调绑定为委托字段，确保不会被GC回收
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    // ======================== 键盘钩子 ========================

    /// <summary>
    /// 安装键盘低层钩子（WH_KEYBOARD_LL）
    /// 安装后所有键盘输入先经过此回调，返回非0值则丢弃消息
    /// </summary>
    /// <returns>true 安装成功，false 失败</returns>
    public bool InstallKeyboardHook()
    {
        lock (_lock)
        {
            if (_keyboardHookHandle != IntPtr.Zero)
            {
                LogMessage?.Invoke("[警告] 键盘钩子已安装，跳过重复安装");
                return true;
            }

            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            if (curModule == null)
            {
                LogMessage?.Invoke("[错误] 无法获取当前进程主模块");
                return false;
            }

            IntPtr moduleHandle = NativeMethods.GetModuleHandle(curModule.ModuleName);
            _keyboardHookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _keyboardProc,
                moduleHandle,
                0);

            if (_keyboardHookHandle == IntPtr.Zero)
            {
                int errCode = Marshal.GetLastWin32Error();
                LogMessage?.Invoke($"[错误] 键盘钩子安装失败，错误码: {errCode}");
                return false;
            }

            IsKeyboardBlocked = true;
            LogMessage?.Invoke("[成功] 键盘钩子已安装，所有按键将被拦截（除解锁热键 Ctrl+Alt+Shift+F8）");
            return true;
        }
    }

    /// <summary>
    /// 卸载键盘低层钩子
    /// </summary>
    public void UninstallKeyboardHook()
    {
        lock (_lock)
        {
            if (_keyboardHookHandle == IntPtr.Zero) return;

            bool result = NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
            IsKeyboardBlocked = false;

            if (result)
                LogMessage?.Invoke("[成功] 键盘钩子已卸载，键盘输入已恢复");
            else
                LogMessage?.Invoke($"[错误] 键盘钩子卸载失败，错误码: {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// 键盘钩子回调函数
    /// 核心逻辑：检查是否按下解锁热键 → 是则设置解锁标志并放行 → 否则拦截
    /// 注意：此函数在UI线程的消息泵上下文中被调用，必须快速返回
    /// </summary>
    /// <param name="nCode">HC_ACTION 表示正常消息，小于0表示系统消息</param>
    /// <param name="wParam">消息类型（WM_KEYDOWN / WM_KEYUP 等）</param>
    /// <param name="lParam">指向 KBDLLHOOKSTRUCT 结构体的指针</param>
    /// <returns>非0值拦截消息，CallNextHookEx 结果放行</returns>
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 只在键盘屏蔽开启且消息有效时拦截
        if (nCode >= NativeMethods.HC_ACTION && IsKeyboardBlocked)
        {
            var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            bool isKeyDown = wParam == NativeMethods.WM_KEYDOWN ||
                             wParam == NativeMethods.WM_SYSKEYDOWN;
            bool isKeyUp   = wParam == NativeMethods.WM_KEYUP ||
                             wParam == NativeMethods.WM_SYSKEYUP;

            Keys key = (Keys)kb.vkCode;

            // 追踪修饰键物理状态（钩子回调内自行追踪，比 GetAsyncKeyState 更可靠）
            // 注意：处理左右两侧的 Ctrl/Alt/Shift
            if (key == Keys.LControlKey || key == Keys.RControlKey)
                _ctrlDown = isKeyDown;
            else if (key == Keys.LMenu || key == Keys.RMenu)
                _altDown = isKeyDown;
            else if (key == Keys.LShiftKey || key == Keys.RShiftKey)
                _shiftDown = isKeyDown;

            // 检测解锁热键：只有按键按下时检查（避免重复触发）
            if (isKeyDown && key == UnlockKey && CheckModifiers())
            {
                _unlockRequested = true;
                LogMessage?.Invoke("[热键] 检测到解锁热键 Ctrl+Alt+Shift+F8，即将解除屏蔽");
                // 放行此按键（让系统正常处理）
                return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            // 拦截所有其他按键（返回非0值丢弃消息）
            return (IntPtr)1;
        }

        // 未屏蔽时放行所有消息
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // ======================== 鼠标钩子 ========================

    /// <summary>
    /// 安装鼠标低层钩子（WH_MOUSE_LL）
    /// 安装后所有鼠标事件（移动/点击/滚轮）先经过此回调
    /// </summary>
    /// <returns>true 安装成功，false 失败</returns>
    public bool InstallMouseHook()
    {
        lock (_lock)
        {
            if (_mouseHookHandle != IntPtr.Zero)
            {
                LogMessage?.Invoke("[警告] 鼠标钩子已安装，跳过重复安装");
                return true;
            }

            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            if (curModule == null)
            {
                LogMessage?.Invoke("[错误] 无法获取当前进程主模块");
                return false;
            }

            IntPtr moduleHandle = NativeMethods.GetModuleHandle(curModule.ModuleName);
            _mouseHookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL,
                _mouseProc,
                moduleHandle,
                0);

            if (_mouseHookHandle == IntPtr.Zero)
            {
                int errCode = Marshal.GetLastWin32Error();
                LogMessage?.Invoke($"[错误] 鼠标钩子安装失败，错误码: {errCode}");
                return false;
            }

            IsMouseBlocked = true;
            LogMessage?.Invoke("[成功] 鼠标钩子已安装，所有鼠标操作将被拦截");
            return true;
        }
    }

    /// <summary>
    /// 卸载鼠标低层钩子
    /// </summary>
    public void UninstallMouseHook()
    {
        lock (_lock)
        {
            if (_mouseHookHandle == IntPtr.Zero) return;

            bool result = NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
            IsMouseBlocked = false;

            if (result)
                LogMessage?.Invoke("[成功] 鼠标钩子已卸载，鼠标操作已恢复");
            else
                LogMessage?.Invoke($"[错误] 鼠标钩子卸载失败，错误码: {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// 鼠标钩子回调函数
    /// 核心逻辑：屏蔽所有鼠标消息（移动、点击、滚轮）
    /// 注意：鼠标移动到本窗体上时放行，以便用户能操作界面
    /// </summary>
    /// <param name="nCode">HC_ACTION 表示正常消息</param>
    /// <param name="wParam">鼠标消息类型</param>
    /// <param name="lParam">指向 MSLLHOOKSTRUCT 结构体的指针</param>
    /// <returns>非0值拦截消息</returns>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= NativeMethods.HC_ACTION && IsMouseBlocked)
        {
            var ms = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // 判断鼠标是否在屏蔽器窗体上（用户可能需要点击停止按钮）
            // 注意：仅对移动和点击消息做窗口判断，滚轮全局拦截
            if (wParam == NativeMethods.WM_MOUSEMOVE ||
                wParam == NativeMethods.WM_LBUTTONDOWN ||
                wParam == NativeMethods.WM_LBUTTONUP ||
                wParam == NativeMethods.WM_RBUTTONDOWN ||
                wParam == NativeMethods.WM_RBUTTONUP)
            {
                IntPtr hwndUnderCursor = NativeMethods.WindowFromPoint(ms.pt);
                if (hwndUnderCursor == _mainFormHandle)
                {
                    // 鼠标在屏蔽器窗体上，放行
                    return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }
            }

            // 拦截所有鼠标消息
            return (IntPtr)1;
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // ======================== 光标控制（辅助功能） ========================

    /// <summary>
    /// 隐藏鼠标光标（使用 ShowCursor API 的引用计数机制）
    /// 注意：必须与 ShowCursor 配对调用，每次 Hide 对应一次 Show
    /// </summary>
    public void HideCursor()
    {
        if (IsCursorHidden) return;

        // 循环调用直到计数器归零，确保光标完全隐藏
        int count;
        do
        {
            count = NativeMethods.ShowCursor(false);
        }
        while (count >= 0);

        _cursorHideCount = -count; // 记录隐藏次数，用于恢复
        IsCursorHidden = true;
        LogMessage?.Invoke($"[光标] 鼠标光标已隐藏（隐藏次数: {_cursorHideCount}）");
    }

    /// <summary>
    /// 恢复显示鼠标光标
    /// 注意：调用次数必须与 HideCursor 的隐藏次数匹配
    /// </summary>
    public void ShowCursor()
    {
        if (!IsCursorHidden) return;

        for (int i = 0; i < _cursorHideCount; i++)
        {
            NativeMethods.ShowCursor(true);
        }
        _cursorHideCount = 0;
        IsCursorHidden = false;
        LogMessage?.Invoke("[光标] 鼠标光标已恢复显示");
    }

    /// <summary>
    /// 将鼠标光标限制在指定矩形区域内（屏幕坐标）
    /// 例：ClipCursorToArea(100, 100, 200, 200) 将光标限制在 100x100 的矩形内
    /// </summary>
    /// <param name="left">区域左边界（屏幕坐标）</param>
    /// <param name="top">区域上边界（屏幕坐标）</param>
    /// <param name="right">区域右边界（屏幕坐标）</param>
    /// <param name="bottom">区域下边界（屏幕坐标）</param>
    public void ClipCursorToArea(int left, int top, int right, int bottom)
    {
        var rect = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
        NativeMethods.ClipCursor(ref rect);
        IsCursorClipped = true;
        LogMessage?.Invoke($"[光标] 鼠标已限制在区域 ({left},{top})-({right},{bottom})");
    }

    /// <summary>
    /// 解除光标区域限制
    /// </summary>
    public void UnclipCursor()
    {
        NativeMethods.ClipCursor(IntPtr.Zero);
        IsCursorClipped = false;
        LogMessage?.Invoke("[光标] 鼠标区域限制已解除");
    }

    // ======================== 便捷方法 ========================

    /// <summary>
    /// 一键安装键盘+鼠标双钩子
    /// </summary>
    /// <returns>是否全部安装成功</returns>
    public bool StartAll()
    {
        bool kbOk = InstallKeyboardHook();
        bool msOk = InstallMouseHook();
        return kbOk && msOk;
    }

    /// <summary>
    /// 一键卸载所有钩子并恢复光标
    /// 此方法作为终极兜底，不抛异常，确保所有资源被释放
    /// </summary>
    public void StopAll()
    {
        try { UninstallKeyboardHook(); }
        catch (Exception ex) { LogMessage?.Invoke($"[异常] 卸载键盘钩子时出错: {ex.Message}"); }

        try { UninstallMouseHook(); }
        catch (Exception ex) { LogMessage?.Invoke($"[异常] 卸载鼠标钩子时出错: {ex.Message}"); }

        try { ShowCursor(); }
        catch (Exception ex) { LogMessage?.Invoke($"[异常] 恢复光标显示时出错: {ex.Message}"); }

        try { UnclipCursor(); }
        catch (Exception ex) { LogMessage?.Invoke($"[异常] 解除光标限制时出错: {ex.Message}"); }

        _unlockRequested = false;
        LogMessage?.Invoke("[完成] 所有钩子已卸载，光标已恢复，输入已完全恢复");
    }

    // ======================== IDisposable 实现 ========================

    /// <summary>
    /// 释放所有资源（兜底保护）
    /// 使用 using 语句或显式调用确保钩子被卸载
    /// </summary>
    public void Dispose()
    {
        StopAll();
        GC.SuppressFinalize(this);
    }
}