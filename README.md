# Klee Codex 额度任务栏（非官方）

[![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11)](https://www.microsoft.com/windows/windows-11)
[![MIT License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Latest release](https://img.shields.io/github/v/release/LRF0131/klee-codex-quota-widget)](https://github.com/LRF0131/klee-codex-quota-widget/releases/latest)

一个轻量的 Windows 11 任务栏组件，实时显示 Codex 5 小时与 7 天剩余额度、任务运行状态、可用重置次数，以及经过严格筛选的 Tibo 重置倒计时。

> 本项目是独立的非官方社区项目，与 OpenAI 不存在隶属、赞助、认证或合作关系。OpenAI、ChatGPT、GPT、Codex 及相关图形属于 OpenAI，不包含在本项目 MIT 授权范围内。详见 [NOTICE.md](NOTICE.md)。

Windows 11 左下角常驻显示本机 Codex 额度：

```text
GPT  5h 76%  |  7d 42%
```

Codex 结形 Logo 常驻在 `GPT` 左侧：组件刚启动时静止且没有勾；任务运行时 Logo 本体旋转；任务完成后回正并停止，右下角保留小绿色勾，直到下一项任务开始。动画约 30 帧每秒，每圈约 2 秒，仅更新图标区域。

只有当 Codex Reset Monitor 收录了 **Tibo 本人明确承诺全局重置，并给出可解析的 UTC/PST/PDT/PT 具体时间** 时，才会临时扩展为：

```text
GPT  5h 76%  |  7d 42%  |  Tibo 01:26:18
```

预测概率、`soon`、`maybe`、社区猜测、没有具体时间的“明天重置”均不会触发倒计时。

当 7d 剩余额度低于 20%，且 Codex 官方接口确认账户至少有一次可用重置时，右侧显示：

```text
GPT  5h 31%  |  7d 19%  |  可重置 ×1
```

重置次数减少且额度同时明显恢复后，绿色显示 `额度已恢复` 五分钟。组件不会自动使用重置机会。

| 可用重置提醒 | 重置成功确认 |
| --- | --- |
| ![可重置状态](docs/reset-available.png) | ![额度已恢复状态](docs/reset-restored.png) |

## 快速安装

1. 在 Windows 11 上安装并登录 Codex，确认能正常显示 Usage/额度。
2. 从 [Releases](https://github.com/LRF0131/klee-codex-quota-widget/releases/latest) 下载最新版 ZIP。
3. 完整解压后双击 `Install.cmd`，无需管理员权限。
4. 卸载时双击 `Uninstall.cmd`。

发布包未经商业代码签名，Windows 可能提示“未知发布者”。请对照 Release 中的 SHA-256 校验值，不要关闭 Defender，也不要运行哈希不一致的文件。

## 使用

已构建版本位于 `dist\KleeCodexQuotaWidget.exe`。应用依赖本机已安装并使用 ChatGPT 登录的 Codex CLI。

- 左键拖动：微调任务栏横向位置。
- 双击：立即刷新。
- 右键：刷新、打开监控源、恢复左下位置、切换开机自启动或退出。
- 数字只采用 OpenAI 当前账户返回的服务器实值，不做本地估算或平滑插值。
- 检测到本机 Codex 对话活动后，静默防抖 3 秒并立即刷新；持续使用时每 30 秒兜底刷新，空闲时每 1 分钟刷新。
- 任务状态直接读取本机 Codex 会话中的 `task_started` / `task_complete` 事件；同时运行多个任务时，全部结束后才显示完成勾。
- Tibo 信号每 5 分钟刷新；到达预告时间后额度每 15 秒刷新。
- 网络失败会保留最后一次服务器实值；右键菜单可查看准确更新时间与连接状态。
- 临时网络故障按 10、20、40、60 秒渐进重试，恢复后自动回到正常刷新节奏。
- 预告窗口内检测到个人额度明显恢复后，显示 `额度已恢复` 五分钟，然后自动隐藏；不认定恢复由 Tibo 触发。
- 7d 剩余低于 20% 时，通过 Codex 官方 `availableCount` 显示 `可重置 ×N`；次数为零、未知或数据过期时保持隐藏。
- 只有重置次数确实减少且 5h/7d 同时明显恢复，才将其确认为重置成功；自然周期恢复或重置机会过期不会误报。
- Logo 旋转表示运行，小琥珀点表示有明确等待请求，灰叉表示取消/中断/明确终止错误；正常结束显示小绿钩。未写入本机会话日志的等待状态无法可靠识别，普通文本提问不会靠猜测变成等待状态。
- 组件启动时核对当前 Codex 进程启动以来的任务事件，恢复尚未结束的任务；历史完成记录不会让刚启动的组件显示绿钩。
- 额度请求失败或超过两分钟没有成功更新时，数字变灰；成功同步后恢复原色。无悬停详情。
- 自动匹配深浅主题与屏幕 DPI，跟随任务栏自动隐藏；在右键“显示位置”中选择显示器。无副任务栏时回退到主任务栏。
- 界面使用离屏整帧交换并禁用背景擦除；上次成功额度会在本机缓存，重启或短时网络波动时不会先闪成空白值。

重新构建并安装开机启动：

```powershell
.\build.ps1 -Install -Run
```

## 数据和隐私

完整说明见 [PRIVACY.md](PRIVACY.md)。

- 额度优先只读 `auth.json` 中的临时访问令牌和当前账户 ID，通过系统 `curl.exe` 请求 ChatGPT 官方额度接口；7d 低于 20% 时再使用本机 `codex app-server --stdio` 的只读 `account/rateLimits/read` 获取官方重置次数，直接请求失败时也会回退到该通道。
- 令牌仅经标准输入传给 curl，不写入命令行、日志或本应用配置。
- Tibo 信号来自 `https://codexreset.org/` 的公开页面；这是独立社区网站，不隶属于 OpenAI。
- 应用不上传工作区文件，也没有自建服务器或遥测。

## 实现说明

任务栏定位与额度读取思路参考了 MIT 项目 `upstream-ray/codex-usage-monitor` 和 `Tooblippe/codex-usage`，本实现为面向当前需求重新编写的轻量 WinForms 版本。

## 参与贡献

欢迎提交 Issue 和 Pull Request。开始前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)；安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。

源代码采用 [MIT License](LICENSE)。第三方名称、商标和图形不包含在该授权中。
