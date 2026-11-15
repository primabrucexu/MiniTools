# 双屏窗口互换（DualScreenSwap）

一个驻留 Windows 托盘的小工具。按 **Ctrl + Alt + P**，交换两个显示器上各自最靠前的普通应用窗口。

## 使用

1. 双击 `bin\Release\DualScreenSwap.exe`。
2. 在 Windows 的“扩展这些显示器”模式下使用两块屏幕。
3. 按 **Ctrl + Alt + P** 交换窗口；也可右键托盘图标，选择“立即交换”。
4. 右键托盘图标选择“设置…”，选择 Ctrl / Alt / Shift 和主键，点击“保存”。新快捷键立即生效，重启后保留。
5. 右键托盘图标选择“退出”即可结束程序并释放快捷键。

托盘图标可能位于任务栏的隐藏图标区域。双击图标也能打开设置。重复启动会提示程序已在运行。

## 交换规则

- 每个显示器选择最靠前的可见普通应用窗口。窗口跨屏时，以与窗口重叠面积最大的显示器为所属屏幕。
- 非最大化窗口保留原来的物理像素尺寸，以及相对所属显示器左上角的偏移。
- 最大化窗口移动到另一块屏幕后继续最大化，其还原位置也跟随移动。
- 两个目标同时确定后才开始移动；其他窗口的位置保持原状。
- 桌面、任务栏、菜单、工具窗口、隐藏/最小化窗口、其他虚拟桌面的窗口和工具自身不参与选择。
- 面向普通应用窗口；F11 无边框全屏、独占全屏游戏不在支持范围内。
- 仅有一个活动显示器、超过两个显示器，或任一屏没有合适窗口时会提示原因，不进行交换。
- 移动失败时尝试恢复两个目标窗口，并提示结果。

这里的“位置”是相对于屏幕左上角的位置。例如窗口在左屏 `(100, 200)`，交换后仍位于右屏的 `(100, 200)`。目标屏更小时，严格保留尺寸和偏移可能使部分窗口超出可见区域。

## 快捷键冲突

程序在启动和保存新快捷键时通过 Windows `RegisterHotKey` 实际尝试注册，检测已被其他程序注册的全局快捷键。

- 启动注册失败：显示原因，仍可从托盘操作或修改快捷键。
- 修改注册失败：保留原快捷键和配置。
- 配置保存失败：释放刚申请的新快捷键，保留原快捷键。
- 保存成功：释放旧快捷键，启用新快捷键。
- 打开设置期间暂停执行交换，关闭设置后恢复。

普通软件内部的打印、编辑等快捷键不属于全局注册占用，无法由此检测。F12 是 Windows 调试器保留键，不提供为选项。

快捷键配置位于 `%LOCALAPPDATA%\DualScreenSwap\settings.ini`，与程序存放目录分开保存。

## 环境与编译

Windows 10 / 11，.NET Framework 4.8 或 4.8.1。使用 Windows 自带的 Framework C# 编译器，无第三方库或 NuGet 依赖。

在项目目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出为 `bin\Release\DualScreenSwap.exe` 和相邻的 `.exe.config`。请保持两个文件在同一目录。

调试编译：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Debug
```

也可使用安装了 .NET Framework 4.8 开发工具的 Visual Studio 打开 `DualScreenSwap.csproj`。

## 源码

| 文件 | 内容 |
|---|---|
| `Program.cs` | 托盘、消息循环、单实例、设置入口 |
| `WindowSwapper.cs` | 窗口筛选、移动、最大化状态与失败恢复 |
| `Hotkeys.cs` | 快捷键注册、设置窗口与配置保存 |
| `DualScreenSwap.csproj` | Visual Studio / MSBuild 项目 |
| `build.ps1` | 使用本机 Framework 编译器构建 |
| `app.manifest` / `App.config` | 权限、DPI 和运行时设置 |

## 验证与边界

具体测试结果见 `VALIDATION.md`。

程序以普通用户权限运行。如果目标应用以管理员身份运行并拒绝移动，可让本工具使用相同权限运行。程序不会自动提权。

不同应用可能限制窗口尺寸或自行响应 DPI 变化。工具会检查移动结果并在失败时尝试恢复，不能保证所有第三方窗口都接受指定尺寸。不同分辨率和混合缩放场景需要在对应硬件环境中实测。

Windows API 参考：[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[WINDOWPLACEMENT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement)、[SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)。
