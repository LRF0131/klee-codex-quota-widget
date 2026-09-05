# Klee Codex Quota Widget（非官方）

**简体中文** | [English](README.en.md)

[![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11)](https://www.microsoft.com/windows/windows-11)
[![MIT License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Latest release](https://img.shields.io/github/v/release/LRF0131/klee-codex-quota-widget)](https://github.com/LRF0131/klee-codex-quota-widget/releases/latest)

一个常驻在 Windows 11 任务栏左下角的小组件，用来查看 Codex 剩余额度和当前任务状态。

```text
GPT  5h 76%  |  7d 42%
```

它会直接读取当前 Codex 账号的服务器额度，同时在真正需要处理时显示恢复时间、可用重置次数或经过确认的 Tibo 重置预告。平时右侧没有多余文字。

> 本项目是独立的非官方社区项目，与 OpenAI 不存在隶属、赞助、认证或合作关系。OpenAI、ChatGPT、GPT、Codex 及相关图形属于 OpenAI，不包含在本项目的 MIT 授权范围内。详见 [NOTICE.md](NOTICE.md)。

## 功能概览

- 显示 Codex `5h` 与 `7d` 剩余额度，不在本地推算百分比。
- Logo 随 Codex 任务状态变化：运行时旋转，完成时显示绿色勾，等待时显示琥珀点，中断时显示灰色叉。
- `5h` 剩余低于 10% 时显示可靠的恢复时间。
- `7d` 归零时，有可用重置机会则显示 `可重置 ×N`，否则显示可靠的恢复日期。
- 仅在 Tibo 明确给出未来重置时间时显示倒计时，不采纳预测、模糊表述或社区猜测。
- 背景逐像素透明，保留 Windows 11 任务栏自身的颜色和透明效果。
- 支持深浅主题、DPI、多显示器、任务栏自动隐藏和当前用户开机启动。
- 网络失败时保留上一次服务器实值；数据超过两分钟未更新时自动变灰。

| 可用重置提醒 | 恢复确认 |
| --- | --- |
| ![可重置状态](docs/reset-available.png) | ![额度已恢复状态](docs/reset-restored.png) |

## 系统要求

- Windows 11 64 位。
- 已安装 Codex，并已使用 ChatGPT 账号登录。
- Codex 本身能够正常联网并显示 Usage/额度。
- 安装和运行组件不需要管理员权限。

## 安装、更新与卸载

1. 从 [最新 Release](https://github.com/LRF0131/klee-codex-quota-widget/releases/latest) 下载 `KleeCodexQuotaWidget-...zip`。
2. 完整解压 ZIP，不要直接在压缩包内运行文件。
3. 双击 `Install.cmd`。组件会立即启动，并为当前 Windows 用户设置开机启动。

安装位置：

```text
%LOCALAPPDATA%\KleeCodexQuotaWidget\KleeCodexQuotaWidget.exe
```

更新时，解压新版并再次运行新版 `Install.cmd`。卸载时运行 `Uninstall.cmd`；本地额度缓存和日志会保留在安装目录，可按需手动删除。

发布包没有商业代码签名，因此 Windows 可能显示“未知发布者”。请对照 Release 随附的 SHA-256 文件校验下载内容；无需关闭 Windows Defender。

## 使用方式

组件只有 Logo 区域接收鼠标操作：

- 拖动 Logo：调整任务栏横向位置。
- 双击 Logo：立即刷新额度和监控状态。
- 右键 Logo，直接拖动菜单中的**界面缩放**滑杆实时调节 50%–200%，支持方向键微调和“恢复默认”。拖动时菜单保持展开，收起后自动保存，下次启动继续使用。缩放叠加系统 DPI，过大时自动适配任务栏高度与宽度；Logo 点击范围同步变化。菜单跟随系统深浅主题。
- 右键 Logo：打开刷新、监控源、显示器、位置、开机启动和退出菜单。
- 文字及其余透明区域：鼠标穿透，可以正常点击下面的任务栏。

Logo 的独立点击层使用 `1/255` Alpha，让图案中间的透明空隙也容易点击；可见显示层的空白像素为完全透明。

## 右侧状态规则

组件始终只显示一条右侧状态。下面是最常见的额度组合：

| 5h | 7d | 可用重置次数 | 右侧显示 |
| ---: | ---: | ---: | --- |
| 50% | 80% | 0 | 空 |
| 8% | 80% | 0 | `5h 14:35恢复` |
| 8% | 15% | 2 | `5h 14:35恢复` |
| 8% | 1% | 2 | `5h 14:35恢复` |
| 8% | 0% | 2 | `可重置 ×2` |
| 8% | 0% | 0 | `7d 09-08 14:35恢复` |
| 50% | 0% | 2 | `可重置 ×2` |
| 50% | 0% | 0 | `7d 09-08 14:35恢复` |

表中的恢复时间只是格式示例，只有服务器返回了可靠且仍在未来的时间才会显示。

此外还有两类临时状态：

- 确认额度明显上涨时，绿色显示 `额度已恢复` 五分钟。
- Tibo 明确宣布具体的未来重置时间时，显示 `Tibo 01:26:18`；没有明确预告就不显示。

实际优先顺序为：`额度已恢复` → `7d` 归零状态 → Tibo 倒计时 → `5h` 低额度恢复时间 → 空。

细节规则：

- `5h = 10%` 不触发提示；只有低于 10% 才显示。
- `7d` 尚未归零时，即使账号有重置机会，也不显示次数。
- 恢复时间必须来自服务器并且仍在未来；未知、过期或陈旧数据不会生成猜测时间。
- Tibo 倒计时到点后显示 `Tibo 待确认`。如果此时 `5h` 已耗尽并有可靠恢复时间，则优先显示该时间。
- `额度已恢复` 是中性结果，不认定由 Tibo 触发。重置机会减少并且额度明显上涨，或者在有效 Tibo 预告窗口内确认额度上涨时，才会显示。
- 同一次恢复只提示一次，不会因普通刷新反复延长；提示期间额度再次耗尽会立即结束。

## 任务状态规则

| Logo 状态 | 含义 |
| --- | --- |
| 静止、无角标 | 组件刚启动，或尚未观察到新任务 |
| 旋转 | 至少有一个 Codex 任务正在运行 |
| 绿色勾 | 所有已观察到的并行任务均已正常完成 |
| 琥珀点 | Codex 明确处于等待输入状态 |
| 灰色叉 | 任务被取消、中断或明确终止 |

组件读取本机 Codex 会话事件来判断状态。启动时会核对当前 Codex 进程以来的事件并恢复尚未结束的任务；旧的完成记录不会让刚启动的组件显示绿色勾。没有写入会话事件的状态无法可靠识别。

## 刷新与故障处理

- 检测到 Codex 会话活动后，组件防抖约三秒再刷新。
- 持续使用时每 30 秒兜底刷新，空闲时每 60 秒刷新。
- 有 Tibo 预告并到达预告时间后，每 15 秒检查额度是否恢复。
- Tibo 公共信号每五分钟刷新。
- 临时网络故障按 10、20、40、60 秒渐进重试。
- 上一次额度、恢复时间和可用重置次数缓存在 `%LOCALAPPDATA%\KleeCodexQuotaWidget`，避免启动时先闪成空白值。
- 右键菜单会显示最近更新时间和当前连接状态。

如果一直显示 `--`，请先打开 Codex，确认已经登录且 Codex 自身能看到 Usage，然后双击 Logo 刷新。代理或网络设置也必须能让 Codex 正常联网。

## 数据与隐私

应用没有遥测、自建服务器，也不会上传工作区文件或会话正文。完整说明见 [PRIVACY.md](PRIVACY.md)。

- 常规额度通过当前用户的本机 Codex 登录信息请求 ChatGPT 官方额度接口。
- 当需要重置次数或直接请求失败时，使用本机 `codex app-server --stdio` 的只读 `account/rateLimits/read`。
- 访问令牌只通过标准输入交给系统 `curl.exe`，不会出现在命令行、日志、缓存、Release 或仓库中。
- 任务状态只读取本机会话事件类型，不上传会话内容。
- Tibo 信号来自独立社区网站 [Codex Reset Monitor](https://codexreset.org/)，该网站不隶属于 OpenAI。

## 从源码构建

构建环境需要 Windows 11、已安装的 Codex App，以及系统自带的 .NET Framework C# 编译器。构建脚本会从本机官方 Codex 安装目录读取图标并嵌入 EXE；图标文件本身没有提交到仓库。

```powershell
.\build.ps1
```

构建并设置开机启动：

```powershell
.\build.ps1 -Install -Run
```

运行内置状态和绘制自检：

```powershell
.\dist\KleeCodexQuotaWidget.exe --verify-status .\dist\verification
Get-Content .\dist\verification\result.txt
```

当前自检覆盖任务状态、优先级矩阵、10%/0% 边界、跨天与过期时间、恢复去重、透明 Alpha、深浅配色、100%/125%/150%/200% 离屏渲染，以及 app-server 响应解析。1.3.0 已在当前 Windows 11 桌面验证透明显示、Logo 命中、文字穿透和拖动；其他实际 DPI、多显示器与任务栏自动隐藏组合仍欢迎社区测试。

## 项目结构

```text
src/Program.cs          主程序与内置自检
src/app.manifest        Windows 权限、兼容性和 DPI 声明
packaging/              安装、卸载和使用说明
docs/                   README 示例图片
build.ps1               本机构建脚本
PRIVACY.md              数据和隐私说明
SECURITY.md             安全问题报告方式
NOTICE.md               第三方名称与商标说明
```

任务栏定位与额度读取思路参考了 MIT 项目 `upstream-ray/codex-usage-monitor` 和 `Tooblippe/codex-usage`。本项目为独立编写的轻量 WinForms 实现。

## 参与贡献

欢迎提交 Issue 和 Pull Request。提交代码前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)；安全问题请按照 [SECURITY.md](SECURITY.md) 私下报告。

本项目源代码采用 [MIT License](LICENSE)。第三方名称、商标和图形不包含在该授权中。
