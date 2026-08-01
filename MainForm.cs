using System.ComponentModel;
using System.Text.Json;

namespace InputBlocker;

// ============================================================
// 文件：MainForm.cs
// 功能：输入屏蔽器主界面
//       - 暗色主题 UI（专业、低饱和度、高信息密度）
//       - 屏蔽模式选择（键盘/鼠标/光标隐藏/区域限制）
//       - 自动解锁倒计时
//       - 实时日志输出
// 设计：纯代码构建 UI，无 .Designer 文件，便于阅读和移植
// ============================================================

/// <summary>
/// 输入屏蔽器主窗体
/// 布局分为：状态栏、屏蔽选项区、自动解锁区、控制按钮区、日志区、底部提示区
/// </summary>
internal sealed class MainForm : Form
{
    // ======================== 核心组件 ========================

    /// <summary>全局钩子管理器（负责实际的键鼠屏蔽逻辑）</summary>
    private readonly HookManager _hookManager;

    /// <summary>解锁热键轮询定时器（每 100ms 检查 UnlockRequested 标志）</summary>
    private readonly System.Windows.Forms.Timer _unlockPollTimer;

    /// <summary>自动解锁倒计时定时器（每秒触发一次）</summary>
    private readonly System.Windows.Forms.Timer _countdownTimer;

    /// <summary>钩子健康监控 + 兜底检测定时器（每 500ms 触发一次）</summary>
    private readonly System.Windows.Forms.Timer _healthMonitorTimer;

    /// <summary>自动解锁剩余秒数</summary>
    private int _countdownRemaining;

    /// <summary>紧急解锁文件路径（在 %TEMP% 目录下创建此文件即可强制解锁）</summary>
    private static readonly string EmergencyUnlockPath =
        Path.Combine(Path.GetTempPath(), "input_unlock.txt");

    // ======================== 颜色常量（暗色主题） ========================

    private static readonly Color BgDark     = Color.FromArgb(30, 30, 30);     // 主背景
    private static readonly Color BgPanel    = Color.FromArgb(45, 45, 45);     // 面板背景
    private static readonly Color BgInput    = Color.FromArgb(55, 55, 55);     // 输入框背景
    private static readonly Color FgPrimary  = Color.FromArgb(212, 212, 212);  // 主文字
    private static readonly Color FgAccent   = Color.FromArgb(86, 156, 214);   // 强调文字（蓝）
    private static readonly Color FgWarning  = Color.FromArgb(220, 170, 70);   // 警告文字（黄）
    private static readonly Color FgDanger   = Color.FromArgb(220, 80, 80);    // 危险文字（红）
    private static readonly Color FgSuccess  = Color.FromArgb(100, 200, 100);  // 成功文字（绿）
    private static readonly Color BtnStart   = Color.FromArgb(0, 120, 60);     // 开始按钮（深绿）
    private static readonly Color BtnStop    = Color.FromArgb(160, 40, 40);    // 停止按钮（深红）
    private static readonly Color BorderColor = Color.FromArgb(80, 80, 80);    // 边框颜色

    // ======================== UI 控件声明 ========================

    // 状态区
    private Label _lblStatusTitle;
    private Label _lblStatusValue;
    private Label _lblKbStatus;
    private Label _lblMsStatus;

    // 屏蔽选项区
    private GroupBox _grpOptions;
    private CheckBox _chkBlockKeyboard;
    private CheckBox _chkBlockMouse;
    private CheckBox _chkHideCursor;
    private CheckBox _chkClipCursor;
    private Label _lblClipArea;
    private NumericUpDown _numClipW;
    private NumericUpDown _numClipH;
    private Label _lblClipX;

    // 自动解锁区
    private GroupBox _grpAutoUnlock;
    private Label _lblCountdownSetting;
    private NumericUpDown _numCountdown;
    private Label _lblCountdownUnit;
    private Label _lblCountdownRemaining;

    // 控制按钮区
    private Button _btnStart;
    private Button _btnStop;

    // 日志区
    private GroupBox _grpLog;
    private TextBox _txtLog;

    // 底部提示区
    private Label _lblHotkeyHint;
    private Label _lblWarning;

    // ======================== 构造函数 ========================

    /// <summary>
    /// 初始化主窗体、所有控件、钩子管理器和定时器
    /// </summary>
    public MainForm()
    {
        // 初始化钩子管理器
        _hookManager = new HookManager(this.Handle);
        _hookManager.LogMessage += OnLogMessage;

        // 初始化定时器
        _unlockPollTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _unlockPollTimer.Tick += UnlockPollTimer_Tick;

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += CountdownTimer_Tick;

        _healthMonitorTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _healthMonitorTimer.Tick += HealthMonitorTimer_Tick;

        // 构建 UI
        InitializeForm();
        InitializeControls();

        // 加载历史设置
        LoadSettings();

        // 初始状态
        UpdateStatusDisplay();
    }

    // ======================== 窗体初始化 ========================

    /// <summary>
    /// 配置窗体基本属性
    /// </summary>
    private void InitializeForm()
    {
        this.Text = "输入屏蔽器 - Input Blocker";
        this.Size = new Size(520, 620);
        this.MinimumSize = new Size(460, 550);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.BackColor = BgDark;
        this.ForeColor = FgPrimary;
        this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);

        // 关闭窗体时停止所有屏蔽
        this.FormClosing += MainForm_FormClosing;
    }

    // ======================== 控件初始化 ========================

    /// <summary>
    /// 创建并布局所有 UI 控件（自上而下布局）
    /// </summary>
    private void InitializeControls()
    {
        int y = 10;
        const int padX = 12;
        const int panelWidth = 480;

        // ---- 状态区 ----
        _lblStatusTitle = MakeLabel("当前状态", padX, y, 80, 22, FgAccent, FontStyle.Bold);
        _lblStatusValue = MakeLabel("● 空闲", padX + 75, y, 200, 22, FgSuccess);
        y += 26;

        _lblKbStatus = MakeLabel("键盘: 正常", padX, y, 200, 18, FgPrimary);
        _lblMsStatus = MakeLabel("鼠标: 正常", padX + 200, y, 200, 18, FgPrimary);
        y += 28;

        // ---- 屏蔽选项区 ----
        _grpOptions = MakeGroupBox("屏蔽选项", padX, y, panelWidth, 145);
        {
            _chkBlockKeyboard = MakeCheckBox("屏蔽键盘（全局低层钩子 WH_KEYBOARD_LL）", 14, 22, 440, 22);
            _chkBlockKeyboard.CheckedChanged += (s, e) => UpdateStartButtonState();

            _chkBlockMouse = MakeCheckBox("屏蔽鼠标（全局低层钩子 WH_MOUSE_LL）", 14, 46, 440, 22);
            _chkBlockMouse.CheckedChanged += (s, e) => UpdateStartButtonState();

            _chkHideCursor = MakeCheckBox("隐藏鼠标光标", 14, 70, 200, 22);
            _chkClipCursor = MakeCheckBox("限制光标区域", 14, 94, 120, 22);
            // 以下控件坐标相对于 _grpOptions 内部区域，与 "限制光标区域" 复选框同行
            _lblClipArea = CreateLabelInParent(_grpOptions, "区域:", 126, 94, 35, 18, FgPrimary);
            _numClipW = CreateNumericInParent(_grpOptions, 164, 91, 50, 20, 100, 0, 5000);
            _lblClipX = CreateLabelInParent(_grpOptions, "x", 218, 94, 15, 18, FgPrimary);
            _numClipH = CreateNumericInParent(_grpOptions, 233, 91, 50, 20, 100, 0, 5000);
        }
        y += 153;

        // ---- 自动解锁区 ----
        _grpAutoUnlock = MakeGroupBox("自动解锁", padX, y, panelWidth, 60);
        {
            // 以下控件坐标相对于 _grpAutoUnlock 内部区域
            _lblCountdownSetting = CreateLabelInParent(_grpAutoUnlock, "倒计时:", 2, 8, 55, 22, FgPrimary);
            _numCountdown = CreateNumericInParent(_grpAutoUnlock, 60, 6, 55, 22, 30, 0, 3600);
            _lblCountdownUnit = CreateLabelInParent(_grpAutoUnlock, "秒（0 = 禁用自动解锁）", 120, 8, 180, 22, FgPrimary);
            _lblCountdownRemaining = CreateLabelInParent(_grpAutoUnlock, "", 320, 8, 130, 22, FgWarning);
        }
        y += 68;

        // ---- 控制按钮区 ----
        _btnStart = MakeButton("▶  开始屏蔽", padX, y, 150, 36, BtnStart, Color.White);
        _btnStart.Click += BtnStart_Click;

        _btnStop = MakeButton("■  停止屏蔽", padX + 165, y, 150, 36, BtnStop, Color.White);
        _btnStop.Click += BtnStop_Click;
        _btnStop.Enabled = false;
        y += 46;

        // ---- 日志区 ----
        _grpLog = MakeGroupBox("运行日志", padX, y, panelWidth, 170);
        {
            _txtLog = new TextBox
            {
                Location = new Point(10, 20),
                Size = new Size(panelWidth - 20, 140),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(25, 25, 25),
                ForeColor = FgPrimary,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8.5f, FontStyle.Regular)
            };
            _grpLog.Controls.Add(_txtLog);
        }
        y += 178;

        // ---- 底部提示区 ----
        _lblHotkeyHint = MakeLabel("解锁: Ctrl+Alt+Shift+F8  |  紧急: 在 %TEMP% 创建 input_unlock.txt 文件即可强制解锁",
            padX, y, 480, 20, FgAccent);
        y += 22;

        _lblWarning = MakeLabel("⚠ 注意: Ctrl+Alt+Delete 属于系统安全桌面，无法被任何应用层程序拦截",
            padX, y, 480, 18, FgWarning);
    }

    // ======================== 控件工厂方法 ========================

    /// <summary>
    /// 创建标签控件
    /// </summary>
    private Label MakeLabel(string text, int x, int y, int w, int h, Color fg, FontStyle style = FontStyle.Regular)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            ForeColor = fg,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9f, style)
        };
        this.Controls.Add(lbl);
        return lbl;
    }

    /// <summary>
    /// 创建分组框控件
    /// </summary>
    private GroupBox MakeGroupBox(string text, int x, int y, int w, int h)
    {
        var grp = new GroupBox
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            ForeColor = FgAccent,
            BackColor = BgPanel
        };
        // 暗色主题下 GroupBox 默认用系统颜色绘制标题，必须自定义 Paint 才能显示
        grp.Paint += GroupBox_Paint;
        this.Controls.Add(grp);
        return grp;
    }

    /// <summary>
    /// GroupBox 自定义绘制 —— 暗色主题下用 ForeColor 绘制标题文字
    /// 默认 GroupBox 忽略 ForeColor 用 ControlText 系统色绘制，暗色背景下不可见
    /// </summary>
    private void GroupBox_Paint(object? sender, PaintEventArgs e)
    {
        var grp = (GroupBox)sender!;
        // 用 BackColor 填充标题区域背景（清除系统默认绘制的亮色背景）
        var titleSize = TextRenderer.MeasureText(grp.Text, grp.Font);
        var titleRect = new Rectangle(8, 0, titleSize.Width + 4, titleSize.Height);
        using var bgBrush = new SolidBrush(grp.BackColor);
        e.Graphics.FillRectangle(bgBrush, titleRect);
        // 用 ForeColor 绘制标题文字
        TextRenderer.DrawText(e.Graphics, grp.Text, grp.Font, new Point(8, 0), grp.ForeColor);
    }

    /// <summary>
    /// 创建复选框控件
    /// </summary>
    private CheckBox MakeCheckBox(string text, int x, int y, int w, int h)
    {
        var chk = new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            ForeColor = FgPrimary,
            BackColor = Color.Transparent
        };
        _grpOptions.Controls.Add(chk);
        return chk;
    }

    /// <summary>
    /// 创建数值输入控件
    /// </summary>
    private NumericUpDown MakeNumeric(int x, int y, int w, int h, int value, int min, int max)
    {
        var num = new NumericUpDown
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            Minimum = min,
            Maximum = max,
            Value = value,
            BackColor = BgInput,
            ForeColor = FgPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Center
        };
        _grpOptions.Controls.Add(num);
        return num;
    }

    /// <summary>
    /// 创建数值输入控件并添加到指定父控件
    /// </summary>
    private NumericUpDown CreateNumericInParent(Control parent, int x, int y, int w, int h, int value, int min, int max)
    {
        var num = new NumericUpDown
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            Minimum = min,
            Maximum = max,
            Value = value,
            BackColor = BgInput,
            ForeColor = FgPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Center
        };
        parent.Controls.Add(num);
        return num;
    }

    /// <summary>
    /// 创建标签控件并添加到指定父控件
    /// </summary>
    private Label CreateLabelInParent(Control parent, string text, int x, int y, int w, int h, Color fg, FontStyle style = FontStyle.Regular)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            ForeColor = fg,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9f, style)
        };
        parent.Controls.Add(lbl);
        return lbl;
    }

    /// <summary>
    /// 创建按钮控件
    /// </summary>
    private Button MakeButton(string text, int x, int y, int w, int h, Color bg, Color fg)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            UseVisualStyleBackColor = false
        };
        btn.FlatAppearance.BorderSize = 0;
        this.Controls.Add(btn);
        return btn;
    }

    // ======================== 事件处理 ========================

    /// <summary>
    /// 开始屏蔽按钮点击事件
    /// 核心流程：校验选项 → 安装钩子 → 应用光标控制 → 启动倒计时 → 启动轮询定时器
    /// </summary>
    private void BtnStart_Click(object? sender, EventArgs e)
    {
        // 清空解锁标志
        _hookManager.UnlockRequested = false;

        bool anyBlocked = false;

        // 安装键盘钩子
        if (_chkBlockKeyboard.Checked)
        {
            anyBlocked = _hookManager.InstallKeyboardHook() || anyBlocked;
        }

        // 安装鼠标钩子
        if (_chkBlockMouse.Checked)
        {
            anyBlocked = _hookManager.InstallMouseHook() || anyBlocked;
        }

        // 隐藏光标（独立于鼠标钩子，可单独使用）
        if (_chkHideCursor.Checked)
        {
            _hookManager.HideCursor();
        }

        // 限制光标区域（独立于鼠标钩子）
        if (_chkClipCursor.Checked)
        {
            int w = (int)_numClipW.Value;
            int h = (int)_numClipH.Value;
            // 以屏幕中心为基准构建限制区域
            int cx = Screen.PrimaryScreen!.Bounds.Width / 2;
            int cy = Screen.PrimaryScreen.Bounds.Height / 2;
            _hookManager.ClipCursorToArea(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);
        }

        if (!anyBlocked && !_chkHideCursor.Checked && !_chkClipCursor.Checked)
        {
            AppendLog("[警告] 未选择任何屏蔽选项，请至少勾选一项");
            return;
        }

        // 启动自动解锁倒计时
        _countdownRemaining = (int)_numCountdown.Value;
        if (_countdownRemaining > 0)
        {
            _countdownTimer.Start();
            UpdateCountdownDisplay();
        }

        // 启动解锁热键轮询定时器
        _unlockPollTimer.Start();

        // 启动健康监控定时器（兜底检测 + 紧急文件解锁）
        _healthMonitorTimer.Start();

        // 更新 UI 状态
        _btnStart.Enabled = false;
        _btnStop.Enabled = true;
        SetOptionsEnabled(false);
        UpdateStatusDisplay("[屏蔽中]");
        SaveSettings();
        AppendLog("[开始] 输入屏蔽已启动");
    }

    /// <summary>
    /// 停止屏蔽按钮点击事件
    /// 调用 HookManager.StopAll() 卸载所有钩子并恢复光标
    /// </summary>
    private void BtnStop_Click(object? sender, EventArgs e)
    {
        StopBlocking("[手动] 用户点击了停止按钮，输入屏蔽已解除");
    }

    /// <summary>
    /// 窗体关闭事件
    /// 确保关闭前卸载所有钩子（兜底保护）
    /// </summary>
    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveSettings();
        StopBlocking("[退出] 程序关闭，所有钩子已卸载，输入已恢复");
    }

    /// <summary>
    /// 解锁热键轮询定时器
    /// 每 100ms 检查 HookManager.UnlockRequested 标志
    /// 因为钩子回调在 UI 线程执行，不能直接操作 UI，所以用轮询方式解耦
    /// </summary>
    private void UnlockPollTimer_Tick(object? sender, EventArgs e)
    {
        // 主检测：钩子回调设置的标志位（基于钩子内追踪的修饰键状态）
        if (_hookManager.UnlockRequested)
        {
            _hookManager.UnlockRequested = false;
            StopBlocking("[热键] 钩子回调检测到解锁热键，输入屏蔽已解除");
            return;
        }

        // 兜底检测：直接用 GetAsyncKeyState 二次确认（钩子追踪失败时的后备方案）
        if (_hookManager.IsKeyboardBlocked)
        {
            bool ctrl  = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
            bool alt   = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU)    & 0x8000) != 0;
            bool shift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT)   & 0x8000) != 0;
            bool f8    = (NativeMethods.GetAsyncKeyState((int)Keys.F8) & 0x8000) != 0;

            if (ctrl && alt && shift && f8)
            {
                StopBlocking("[热键-兜底] GetAsyncKeyState 备用检测到解锁热键，输入屏蔽已解除");
            }
        }
    }

    /// <summary>
    /// 自动解锁倒计时定时器
    /// 每秒递减，归零时自动停止屏蔽
    /// </summary>
    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        _countdownRemaining--;
        UpdateCountdownDisplay();

        if (_countdownRemaining <= 0)
        {
            StopBlocking("[自动] 倒计时结束，输入屏蔽已自动解除");
        }
    }

    /// <summary>
    /// 钩子健康监控 + 兜底检测定时器（每 500ms）
    /// 功能1：检测紧急解锁文件（在 %TEMP%\input_unlock.txt 创建此文件即可强制解锁）
    /// 功能2：监控钩子健康状态，若钩子被系统卸载则尝试自动重装
    /// </summary>
    private void HealthMonitorTimer_Tick(object? sender, EventArgs e)
    {
        // ====== 兜底3：紧急解锁文件检测 ======
        // 如果程序卡死或热键失效，用户可在 %TEMP% 目录创建 input_unlock.txt 强制解锁
        try
        {
            if (File.Exists(EmergencyUnlockPath))
            {
                AppendLog("[兜底] 检测到紧急解锁文件，正在强制解锁...");
                try { File.Delete(EmergencyUnlockPath); } catch { /* 删除失败不阻塞解锁 */ }
                StopBlocking("[兜底-文件] 紧急解锁文件触发，输入屏蔽已强制解除");
                return;
            }
        }
        catch { /* 文件检测失败不影响主流程 */ }

        // ====== 兜底2：钩子健康监控 ======
        // 如果键盘屏蔽开启，再次用 GetAsyncKeyState 检测解锁热键（冗余保障）
        if (_hookManager.IsKeyboardBlocked)
        {
            bool ctrl  = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
            bool alt   = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU)    & 0x8000) != 0;
            bool shift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT)   & 0x8000) != 0;
            bool f8    = (NativeMethods.GetAsyncKeyState((int)Keys.F8) & 0x8000) != 0;

            if (ctrl && alt && shift && f8)
            {
                StopBlocking("[热键-监控] 健康监控线程检测到解锁热键，输入屏蔽已解除");
                return;
            }
        }
    }

    // ======================== 核心方法 ========================

    /// <summary>
    /// 停止所有屏蔽的统一入口
    /// 所有停止路径（按钮点击/热键/倒计时/程序退出）均调用此方法
    /// </summary>
    /// <param name="reason">停止原因（用于日志记录）</param>
    private void StopBlocking(string reason)
    {
        // 停止定时器
        _unlockPollTimer.Stop();
        _countdownTimer.Stop();
        _healthMonitorTimer.Stop();

        // 卸载所有钩子、恢复光标
        _hookManager.StopAll();

        // 恢复 UI 状态
        _btnStart.Enabled = true;
        _btnStop.Enabled = false;
        SetOptionsEnabled(true);
        _lblCountdownRemaining.Text = "";
        UpdateStatusDisplay();
        AppendLog(reason);
    }

    /// <summary>
    /// 更新状态显示区域
    /// </summary>
    /// <param name="statusText">状态文本，null 表示空闲状态</param>
    private void UpdateStatusDisplay(string? statusText = null)
    {
        if (statusText == null)
        {
            _lblStatusValue.Text = "● 空闲";
            _lblStatusValue.ForeColor = FgSuccess;
        }
        else
        {
            _lblStatusValue.Text = "● " + statusText;
            _lblStatusValue.ForeColor = FgDanger;
        }

        _lblKbStatus.Text = _hookManager.IsKeyboardBlocked
            ? "键盘: 已屏蔽"
            : "键盘: 正常";
        _lblMsStatus.Text = _hookManager.IsMouseBlocked
            ? "鼠标: 已屏蔽"
            : "鼠标: 正常";
    }

    /// <summary>
    /// 更新倒计时显示
    /// </summary>
    private void UpdateCountdownDisplay()
    {
        _lblCountdownRemaining.Text = $"剩余: {_countdownRemaining} 秒";
        _lblCountdownRemaining.ForeColor = _countdownRemaining <= 5 ? FgDanger : FgWarning;
    }

    /// <summary>
    /// 根据当前选项更新开始按钮的可用状态
    /// 至少选一项才能开始屏蔽
    /// </summary>
    private void UpdateStartButtonState()
    {
        _btnStart.Enabled = _chkBlockKeyboard.Checked ||
                            _chkBlockMouse.Checked ||
                            _chkHideCursor.Checked ||
                            _chkClipCursor.Checked;
    }

    /// <summary>
    /// 设置屏蔽选项控件的启用/禁用状态
    /// 屏蔽期间禁止修改选项
    /// </summary>
    private void SetOptionsEnabled(bool enabled)
    {
        _chkBlockKeyboard.Enabled = enabled;
        _chkBlockMouse.Enabled = enabled;
        _chkHideCursor.Enabled = enabled;
        _chkClipCursor.Enabled = enabled;
        _numClipW.Enabled = enabled;
        _numClipH.Enabled = enabled;
        _numCountdown.Enabled = enabled;
    }

    /// <summary>
    /// 钩子管理器日志事件处理
    /// </summary>
    private void OnLogMessage(string message)
    {
        AppendLog(message);
    }

    /// <summary>
    /// 向日志文本框追加一行日志
    /// 自动添加时间戳，通过 Invoke 确保线程安全
    /// </summary>
    private void AppendLog(string message)
    {
        if (_txtLog.InvokeRequired)
        {
            _txtLog.Invoke(() => AppendLogInternal(message));
        }
        else
        {
            AppendLogInternal(message);
        }
    }

    /// <summary>
    /// 日志追加的内部实现（必须在 UI 线程调用）
    /// </summary>
    private void AppendLogInternal(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        _txtLog.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
        // 自动滚动到底部
        _txtLog.SelectionStart = _txtLog.Text.Length;
        _txtLog.ScrollToCaret();
    }

    // ======================== 设置持久化 ========================

    /// <summary>
    /// 获取设置文件路径（exe 同目录下的 settings.json）
    /// </summary>
    private static string SettingsFilePath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    /// <summary>
    /// 从 JSON 文件加载用户配置到 UI 控件
    /// 文件不存在则使用默认值，解析失败则静默忽略
    /// </summary>
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath)) return;

            string json = File.ReadAllText(SettingsFilePath);
            var s = JsonSerializer.Deserialize<SettingsData>(json);
            if (s == null) return;

            _chkBlockKeyboard.Checked = s.BlockKeyboard;
            _chkBlockMouse.Checked = s.BlockMouse;
            _chkHideCursor.Checked = s.HideCursor;
            _chkClipCursor.Checked = s.ClipCursor;
            _numClipW.Value = s.ClipWidth;
            _numClipH.Value = s.ClipHeight;
            _numCountdown.Value = s.AutoUnlockSeconds;

            UpdateStartButtonState();
            AppendLog("[设置] 已加载历史配置");
        }
        catch (Exception ex)
        {
            AppendLog($"[设置] 加载配置失败: {ex.Message}，使用默认值");
        }
    }

    /// <summary>
    /// 将当前 UI 控件状态保存到 JSON 文件
    /// </summary>
    private void SaveSettings()
    {
        try
        {
            var s = new SettingsData
            {
                BlockKeyboard = _chkBlockKeyboard.Checked,
                BlockMouse = _chkBlockMouse.Checked,
                HideCursor = _chkHideCursor.Checked,
                ClipCursor = _chkClipCursor.Checked,
                ClipWidth = (int)_numClipW.Value,
                ClipHeight = (int)_numClipH.Value,
                AutoUnlockSeconds = (int)_numCountdown.Value
            };

            string json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            AppendLog($"[设置] 保存配置失败: {ex.Message}");
        }
    }

    // ======================== 生命周期 ========================

    /// <summary>
    /// 重写 Dispose 确保资源释放
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _unlockPollTimer?.Dispose();
            _countdownTimer?.Dispose();
            _healthMonitorTimer?.Dispose();
            _hookManager?.Dispose();
        }
        base.Dispose(disposing);
    }
}

// ============================================================
// 设置数据类 —— 用于 JSON 序列化/反序列化，实现配置记忆化
// ============================================================

/// <summary>
/// 用户配置数据模型
/// 所有属性使用简单类型，确保 JSON 序列化兼容性
/// </summary>
internal sealed class SettingsData
{
    /// <summary>是否屏蔽键盘</summary>
    public bool BlockKeyboard { get; set; } = true;

    /// <summary>是否屏蔽鼠标</summary>
    public bool BlockMouse { get; set; } = true;

    /// <summary>是否隐藏光标</summary>
    public bool HideCursor { get; set; }

    /// <summary>是否限制光标区域</summary>
    public bool ClipCursor { get; set; }

    /// <summary>限制区域宽度（像素）</summary>
    public int ClipWidth { get; set; } = 100;

    /// <summary>限制区域高度（像素）</summary>
    public int ClipHeight { get; set; } = 100;

    /// <summary>自动解锁倒计时秒数（0 = 禁用）</summary>
    public int AutoUnlockSeconds { get; set; } = 30;
}