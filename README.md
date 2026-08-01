# InputBlocker - Windows 全局键鼠屏蔽工具

基于 .NET 8 WinForms 的 Windows 全局输入屏蔽工具，使用 `WH_KEYBOARD_LL` / `WH_MOUSE_LL` 低层钩子拦截键盘和鼠标，支持光标隐藏、区域限制，内置多重兜底恢复机制。

## 功能

- **键盘屏蔽**：全局低层钩子 `WH_KEYBOARD_LL` 拦截所有按键
- **鼠标屏蔽**：全局低层钩子 `WH_MOUSE_LL` 拦截所有鼠标移动/点击/滚轮
- **光标隐藏**：`ShowCursor` API 隐藏鼠标光标
- **区域限制**：`ClipCursor` API 限制鼠标移动范围
- **自动解锁**：可配置倒计时自动解除屏蔽
- **设置记忆**：配置自动保存到 `settings.json`

## 退出/恢复方式（6 层兜底）

| 层级 | 方式 | 说明 |
|------|------|------|
| 1 | `Ctrl+Alt+Shift+F8` | 钩子回调内追踪修饰键状态，主通道 |
| 2 | `Ctrl+Alt+Shift+F8` | `GetAsyncKeyState` 二次检测，兜底 |
| 3 | `Ctrl+Alt+Shift+F8` | 独立健康监控定时器再次检测 |
| 4 | 创建 `%TEMP%\input_unlock.txt` | 紧急文件解锁，远程可用 |
| 5 | 自动倒计时 | 可配置秒数，归零自动解锁 |
| 6 | `Ctrl+Alt+Delete` 任务管理器 | 系统安全桌面，永不失效 |

## 编译

```bash
dotnet build -c Release
```

## 运行

- 需要**管理员权限**（app.manifest 已配置自动请求 UAC 提权）
- .NET 8 Runtime

## 项目结构

```
InputBlocker/
├── InputBlocker.csproj    # 项目文件 (.NET 8 WinForms)
├── app.manifest           # 管理员权限清单
├── Program.cs             # 入口 + 权限校验 + 兜底
├── NativeMethods.cs       # P/Invoke 声明
├── HookManager.cs         # 核心钩子管理器
├── MainForm.cs            # 暗色主题主界面
└── SettingsData.cs        # 设置数据模型
```

## 注意事项

- `Ctrl+Alt+Delete` 属于系统安全桌面，无法被任何应用层程序拦截
- 程序崩溃时，重启计算机可 100% 恢复键鼠
- 此工具仅供合法用途使用

## License

MIT