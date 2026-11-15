# 抖音直播清理（DouyinLiveClean）

一个用于 Tampermonkey（油猴）的脚本。当前版本为 **1.4.1**，进入直播间后自动完成四项设置：

- 关闭画面上的弹幕，保留右侧聊天区。
- 开启网页的“屏蔽礼物特效”选项。
- 选择当前可用的最高画质，遵守网页已有的档位限制。
- 自动进入网页全屏，保留浏览器标签栏，沿用网页原生布局并展开聊天区。

## 安装与更新

1. 在浏览器中安装并启用 Tampermonkey。
2. 打开 [GitHub Raw 安装链接](https://raw.githubusercontent.com/primabrucexu/MiniTools/main/DouyinLiveClean/douyin-live-clean.user.js)，在油猴安装页确认安装。
3. 刷新抖音页面。也可手动选择“添加新脚本”，粘贴 [douyin-live-clean.user.js](douyin-live-clean.user.js) 的全部内容，按 `Ctrl + S` 保存。

**旧版首次升级到 1.4.1 时，需要手动替换已有脚本的全部内容，或从 Raw 链接重新安装一次**，让本地脚本获得更新地址。仅修改 GitHub 不会为此前手动粘贴的旧版自动补上更新地址。仅保留一个版本启用，避免不同版本相互干扰。脚本没有外部运行依赖，使用时不需要 Node.js。

脚本的 `@updateURL` 和 `@downloadURL` 均指向上述 `main` 分支 Raw 地址。在油猴设置中启用脚本更新检查，设置检查间隔（例如每天），并确认该脚本允许检查更新；也可以手动执行“检查更新”。自动更新按检查周期发生，不会在 GitHub 推送后立即执行。

以后发布新版时，递增脚本头部的 `@version`，再将改动推送到 `main` 即可，不必创建 GitHub Release。只修改代码而不增加版本号不会正常触发升级。规则见油猴官方的[更新地址说明](https://www.tampermonkey.net/documentation.php?q=update_url)和[版本号说明](https://www.tampermonkey.net/documentation.php?q=version)。

## 生效范围与行为

- 独立直播间：`https://live.douyin.com/直播间编号`。
- 主站关注页直播：`https://www.douyin.com/follow/live/直播间编号`。
- 支持站内导航进入直播间和切换房间。脚本在主站预先加载，以接住不刷新页面的导航；普通短视频页不会执行设置。

脚本直接调用网页原生状态和播放器方法，四项分别等待自己的接口就绪，可以在同一轮发起。实际画质切换仍需等待播放器和网络。

初始化完成后不持续强制设置，可以手动重新开启弹幕或特效、调整画质，也可以通过网页按钮、`Y` 或 `Esc` 退出网页全屏。初始化期间手动操作原有三项控件，或在非输入区域按 `Y`、在网页全屏中按 `Esc`，也会让出对应设置的控制。刷新、换房或播放器实例更换时重新初始化。

已处于网页全屏时不重复切换。若当前房间未开放原生全屏权限，或已处于浏览器全屏、画中画模式，本房间跳过自动进入网页全屏，其他三项照常执行。

页面可见且接口未就绪时，会尝试等待约 30 秒；某项失败不阻塞其他项。缺少原生接口时会报告未完成项目，不会退回模拟鼠标点击。

## 源码与测试

| 文件 | 内容 |
| --- | --- |
| [douyin-live-clean.user.js](douyin-live-clean.user.js) | 安装到 Tampermonkey 的完整脚本 |
| [tests/test-native-api.cjs](tests/test-native-api.cjs) | 受控原生 API 回归测试 |
| [tests/package.json](tests/package.json) / [package-lock.json](tests/package-lock.json) | 测试依赖及锁定版本 |
| [安装说明.txt](安装说明.txt) | 原生接口说明、排查方法和客户端源码依据 |
| [COPYING.txt](COPYING.txt) | GPL-3.0 许可全文 |

测试使用 jsdom 31.0.1。根据当前锁文件，其 Node.js 版本要求为 `^22.22.2 || ^24.15.0 || >=26.0.0`。

在 **DouyinLiveClean 目录**执行：

```powershell
npm ci --prefix .\tests
node --check .\douyin-live-clean.user.js
node .\tests\test-native-api.cjs
```

测试依赖已安装且未变更时，可跳过第一条命令。`tests/node_modules/` 由项目根目录的 `.gitignore` 排除。

v1.4.1 已通过 JavaScript 语法检查及 **34 项受控 API 回归测试**，覆盖原有三项功能以及网页全屏同轮执行、接口与状态延迟、手动退出、换房、权限限制、其他播放模式保持，以及 `Y` / `Esc` 按键边界。两种入口的受控环境中，四项在接口就绪后的同一轮发起。

原有 v1.3 功能已获得用户实际使用的良好反馈；网页全屏尚未在登录后的真实直播间完成验证。受控测试不等同于真实直播间验证。自动更新已核对脚本元数据和 Raw 文件，尚未在油猴中实际验证更新流程。

## 限制与排查

脚本依赖抖音内部状态和播放器接口，这些不是稳定的公开 API，网页改版后可能需要适配。脚本不会自动登录或绕过受限画质档位。

如果某项没有生效：

1. 确认油猴中仅启用了一个版本，刷新并进入上述支持的直播页面。
2. 查看原生设置面板：“弹幕开关”应关闭，“屏蔽礼物特效”应开启，清晰度应为当前可选最高档且不是自动模式；满足权限且未处于其他播放模式时，应进入网页全屏。
3. 打开浏览器开发者工具的控制台，查找包含“抖音直播清爽”的提示。反馈时提供直播间网址及相关提示，便于定位接口变化。

## 来源与许可

按 **GPL-3.0-only** 发布，许可全文见 [COPYING.txt](COPYING.txt)。旧版控件定位适配自 Frequenk 的《网页抖音体验增强》v5.8；当前版本依据抖音客户端实现改用原生方法。来源地址及具体客户端文件记录在[安装说明](安装说明.txt)中，脚本头部保留来源声明。
