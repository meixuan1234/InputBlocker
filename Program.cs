using System.Security.Principal;

namespace InputBlocker;

// ============================================================
// 文件：Program.cs
// 功能：应用程序入口点
//       - 管理员权限校验
//       - 进程退出兜底清理
//       - 全局异常捕获
// 注意：app.manifest 已配置 requireAdministrator，
//       但运行时仍需二次校验以防清单被篡改或兼容性模式绕过
// ============================================================

/// <summary>
/// 应用程序入口类
/// </summary>
internal static class Program
{
    /// <summary>
    /// 应用程序主入口点
    /// 流程：管理员权限检查 → 注册退出钩子 → 启动 WinForms 消息循环
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 启用 Windows 视觉样式（WinForms 标准配置）
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 管理员权限二次校验
        if (!IsAdministrator())
        {
            MessageBox.Show(
                "输入屏蔽器需要管理员权限才能安装全局钩子。\n\n" +
                "请右键点击程序 -> 以管理员身份运行，或重新编译程序。\n\n" +
                "程序将退出。",
                "权限不足 - 输入屏蔽器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        // 注册进程退出事件（兜底保护：即使崩溃也尝试清理）
        // 注意：此事件在非正常退出时可能不触发，仅作为额外保护层
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Application.ApplicationExit += OnApplicationExit;

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            // 全局异常兜底
            MessageBox.Show(
                $"程序发生未处理的异常：\n\n{ex.Message}\n\n" +
                "程序将退出。如果键鼠被屏蔽，请按 Ctrl+Alt+Delete 打开任务管理器结束进程，\n" +
                "或重启计算机恢复。",
                "严重错误 - 输入屏蔽器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 检查当前进程是否以管理员权限运行
    /// 原理：检查 Windows 身份标识中的 BuiltInAdministratorsSid 是否在当前用户令牌中
    /// </summary>
    /// <returns>true 表示以管理员身份运行</returns>
    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // 权限检查失败时保守处理，返回 false 让用户知晓
            return false;
        }
    }

    /// <summary>
    /// 进程退出事件处理（兜底清理）
    /// 注意：Windows 在进程退出时会自动卸载该进程设置的所有钩子，
    /// 但显式调用更可靠，尤其是在钩子句柄损坏时
    /// </summary>
    private static void OnProcessExit(object? sender, EventArgs e)
    {
        // 进程退出时系统会自动清理钩子，此处仅做日志记录
        // 实际钩子卸载在 MainForm.FormClosing 中完成
        System.Diagnostics.Debug.WriteLine("[Program] 进程退出事件触发");
    }

    /// <summary>
    /// 未处理异常事件
    /// 记录异常信息，提示用户恢复方法
    /// </summary>
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[Program] 未处理异常: {(e.ExceptionObject as Exception)?.Message ?? "未知错误"}");
    }

    /// <summary>
    /// 应用程序退出事件
    /// </summary>
    private static void OnApplicationExit(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[Program] 应用程序正常退出");
    }
}