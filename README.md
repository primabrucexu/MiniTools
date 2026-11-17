# MiniTools

个人小工具集合。每个工具放在独立目录中，分别维护源码、使用说明和测试。

| 工具 | 用途 | 环境 |
| --- | --- | --- |
| [DualScreenSwap](DualScreenSwap/README.md) | 快速交换两块显示器上各自最靠前的普通应用窗口 | Windows 10 / 11，.NET Framework 4.8 或 4.8.1 |
| [DouyinLiveClean](DouyinLiveClean/README.md) | 进入抖音直播间后关闭弹幕、屏蔽礼物特效、选择最高可用画质，并进入网页全屏、收起聊天栏、隐藏底部礼物栏 | 安装了 Tampermonkey 的浏览器 |

## 目录

```text
MiniTools/
├── .gitignore                      # 所有工具共用的忽略规则
├── AGENTS.md                       # 项目协作规范
├── README.md
├── DualScreenSwap/
│   ├── *.cs                        # 托盘、快捷键、窗口交换实现
│   ├── DualScreenSwap.csproj
│   ├── build.ps1
│   ├── README.md
│   ├── VALIDATION.md
│   ├── tests/
│   └── bin/Release/                 # 本地可运行程序及配置
└── DouyinLiveClean/
    ├── README.md
    ├── douyin-live-clean.user.js
    ├── 安装说明.txt
    ├── COPYING.txt
    └── tests/
        ├── test-native-api.cjs
        ├── package.json
        └── package-lock.json
```

以下命令均在 MiniTools 根目录执行。

## 双屏窗口交换

双击 `DualScreenSwap\bin\Release\DualScreenSwap.exe` 启动，保留同目录下的 `DualScreenSwap.exe.config`。默认快捷键为 **Ctrl + Alt + P**；右键托盘图标可立即交换窗口、修改快捷键或退出。已有快捷键配置保存在 `%LOCALAPPDATA%\DualScreenSwap\settings.ini`，移动程序目录不影响配置。

工具要求恰好两个活动显示器，并使用扩展桌面。普通窗口保留物理像素尺寸和相对屏幕位置；最大化窗口交换后仍保持最大化。F11 无边框全屏和独占全屏游戏不在支持范围内。不同分辨率、混合缩放及应用自身限制窗口尺寸的情况需要对应环境实测。

构建无需 NuGet 或第三方库，使用本机 .NET Framework C# 编译器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\DualScreenSwap\build.ps1
```

输出目录为 `DualScreenSwap\bin\Release`。完整使用规则见[工具 README](DualScreenSwap/README.md)，原有验证记录见 [VALIDATION.md](DualScreenSwap/VALIDATION.md)。

现有测试会真实注册快捷键、短暂写入并恢复用户配置，并打开测试界面。运行前先退出工具，在可交互的 Windows 桌面执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\DualScreenSwap\tests\run-tests.ps1
```

需要实际双屏窗口测试时，使用以下命令；它会创建并移动测试窗口，需要两块活动显示器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\DualScreenSwap\tests\run-tests.ps1 -IncludeWindowIntegration
```

## 抖音直播清理

当前脚本版本为 **1.4.3**。脚本直接调用网页原生状态和播放器方法，在进入直播间后关闭弹幕、屏蔽礼物特效、选择当前可用的最高画质，并自动进入播放器 `Y` 键对应的网页全屏、收起聊天栏，保留浏览器标签栏和聊天消息。已处于网页全屏时也会收起展开的聊天栏。前三项互不等待，网页全屏和收起聊天栏会等待对应原生控件初始化；完成后可手动展开聊天、调整设置或退出网页全屏，换房、刷新或播放器实例更换时重新初始化。网页全屏受限或已处于浏览器全屏、画中画时，跳过自动进入网页全屏和收起聊天栏。

另用定向样式隐藏视频下方整条礼物栏，包括礼物图标、价格、“更多”和充值入口。普通布局和 Y 网页全屏均由页面原有布局填满释放出的空间；两种直播入口及站内换房持续生效，离开支持的直播路径后移除样式。

安装步骤：

1. 在浏览器中安装并启用 Tampermonkey。
2. 打开 [GitHub Raw 安装链接](https://raw.githubusercontent.com/primabrucexu/MiniTools/main/DouyinLiveClean/douyin-live-clean.user.js)，在油猴安装页确认安装。也可选择“添加新脚本”，粘贴 [douyin-live-clean.user.js](DouyinLiveClean/douyin-live-clean.user.js) 的全部内容并保存。
3. 刷新抖音页面，仅保留一个版本启用。旧版首次升级到 1.4.1 需要手动替换内容或从上述链接重新安装一次，才能获得自动更新地址。

在油猴设置中启用脚本更新检查后，会按设定周期从 GitHub 获取新版。后续发布需递增脚本的 `@version` 并推送到 `main`；完整更新规则见[工具 README](DouyinLiveClean/README.md)。

支持 `https://live.douyin.com/直播间编号` 和 `https://www.douyin.com/follow/live/直播间编号`，包括站内导航进入直播间。脚本匹配整个主站是为了接住站内导航，普通短视频页不会执行设置。

脚本没有外部运行依赖，使用时不需要 Node.js。测试使用 jsdom；当前锁定版本的 Node.js 要求为 `^22.22.2 || ^24.15.0 || >=26.0.0`。安装测试依赖并检查：

```powershell
npm ci --prefix .\DouyinLiveClean\tests
node --check .\DouyinLiveClean\douyin-live-clean.user.js
node .\DouyinLiveClean\tests\test-native-api.cjs
```

v1.4.3 已通过 JavaScript 语法检查及现有 **41 项受控 API 回归测试**。在未登录 Edge 的真实直播间注入完整脚本，已验证原生设置正常、底部礼物栏隐藏，并在 Y 网页全屏及 Esc 退出后的普通布局中确认无底栏留白。另在受控 Edge 页面验证关注页入口的延迟挂载、SPA 换房、离开直播路径移除样式及重新进入恢复隐藏；受控页面不等同于关注页实站验证。原有三项功能已获用户实际使用的良好反馈；尚未覆盖油猴扩展安装、登录会话及 `follow/live` 真实入口。自动更新已核对元数据和 Raw 文件，尚未在油猴中实际验证更新流程。脚本依赖抖音内部接口，网页改版可能需要适配；画质选择和网页全屏遵守页面已有权限限制。详细行为、排查方式和来源见[工具 README](DouyinLiveClean/README.md)。

## 协作与许可

开发和修改请遵循 [AGENTS.md](AGENTS.md)。忽略规则统一维护在根目录 [.gitignore](.gitignore)。构建产物和测试依赖应留在各工具目录内，不提交 `bin/`、`obj/` 或 `node_modules/`。

抖音直播清理脚本按 **GPL-3.0-only** 发布，保留原作者来源说明，许可全文见 [COPYING.txt](DouyinLiveClean/COPYING.txt)。该许可不自动适用于其他工具；本项目未统一指定整体许可证。
