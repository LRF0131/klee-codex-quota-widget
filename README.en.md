# Klee Codex Quota Widget (Unofficial)

[简体中文](README.md) | **English**

[![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11)](https://www.microsoft.com/windows/windows-11)
[![MIT License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Latest release](https://img.shields.io/github/v/release/LRF0131/klee-codex-quota-widget)](https://github.com/LRF0131/klee-codex-quota-widget/releases/latest)

A small Windows 11 taskbar widget that shows your remaining Codex quota and current task status.

```text
GPT  5h 76%  |  7d 42%
```

It reads server-reported quota for the Codex account currently signed in on your PC. When action is useful, the right side can show a recovery time, earned reset count, or a verified Tibo reset countdown. It stays compact the rest of the time.

> This is an independent, unofficial community project. It is not affiliated with, sponsored by, certified by, or partnered with OpenAI. OpenAI, ChatGPT, GPT, Codex, and related graphics belong to OpenAI and are not covered by this project's MIT License. See [NOTICE.md](NOTICE.md).

## Features

- Shows the server-reported remaining quota for the Codex `5h` and `7d` windows, without locally estimating percentages.
- Reflects Codex task activity in the Logo: spinning while running, a green check when complete, an amber dot while waiting, and a gray cross when interrupted.
- Shows a reliable recovery time when the 5-hour quota falls below 10%.
- When the 7-day quota reaches zero, shows `可重置 ×N` (`Reset ×N`) if earned resets are available, or the known recovery date otherwise.
- Shows a Tibo countdown only after a clear announcement with a specific future reset time. Predictions, vague statements, and community guesses are ignored.
- Uses per-pixel transparency so the native Windows 11 taskbar color and transparency remain visible.
- Supports light and dark themes, DPI scaling, multiple monitors, taskbar auto-hide, and per-user startup.
- Keeps the latest server values during network failures and mutes data that has not refreshed for more than two minutes.

| Reset available | Recovery confirmed |
| --- | --- |
| ![Reset available status](docs/reset-available.png) | ![Quota recovered status](docs/reset-restored.png) |

The on-screen status text is currently Chinese. English UI localization is not yet included.

## Requirements

- 64-bit Windows 11.
- Codex installed and signed in with a ChatGPT account.
- Codex itself can connect and display Usage information.
- Administrator privileges are not required to install or run the widget.

## Install, update, and uninstall

1. Download `KleeCodexQuotaWidget-...zip` from the [latest Release](https://github.com/LRF0131/klee-codex-quota-widget/releases/latest).
2. Extract the entire ZIP. Do not run files from inside the archive.
3. Double-click `Install.cmd`. The widget starts immediately and is added to startup for the current Windows user.

Installation path:

```text
%LOCALAPPDATA%\KleeCodexQuotaWidget\KleeCodexQuotaWidget.exe
```

To update, extract the new package and run its `Install.cmd` again. Run `Uninstall.cmd` to remove the program and startup entry. Local quota cache and logs are kept in the installation directory and may be removed manually.

Release binaries are not commercially code-signed, so Windows may display an “Unknown publisher” warning. Compare the download against the SHA-256 file attached to the Release. Windows Defender does not need to be disabled.

## Controls

Only the Logo area receives mouse input:

- Drag the Logo to move the widget horizontally along the taskbar.
- Double-click the Logo to refresh quota and monitor state immediately.
- Right-click the Logo for refresh, monitor source, display, position, startup, and exit options.
- Text and all other transparent areas pass mouse input through to the taskbar below.

The separate Logo input layer uses `1/255` alpha so gaps inside the mark are still easy to click. Empty pixels in the visible display layer are fully transparent.

## Right-side status rules

Only one right-side status is shown at a time. These are the most common quota combinations:

| 5h | 7d | Reset credits | Right-side status |
| ---: | ---: | ---: | --- |
| 50% | 80% | 0 | Empty |
| 8% | 80% | 0 | `5h 14:35恢复` |
| 8% | 15% | 2 | `5h 14:35恢复` |
| 8% | 1% | 2 | `5h 14:35恢复` |
| 8% | 0% | 2 | `可重置 ×2` |
| 8% | 0% | 0 | `7d 09-08 14:35恢复` |
| 50% | 0% | 2 | `可重置 ×2` |
| 50% | 0% | 0 | `7d 09-08 14:35恢复` |

Recovery times in the table are format examples. They appear only when the server provides a reliable timestamp that is still in the future.

Two temporary statuses may also appear:

- After a substantial quota increase is confirmed, `额度已恢复` (quota recovered) appears in green for five minutes.
- When Tibo clearly announces a specific future reset time, the widget shows `Tibo 01:26:18`. It shows no Tibo status without a clear announcement.

The effective priority is: `额度已恢复` → zero-`7d` status → Tibo countdown → low-`5h` recovery time → empty.

Detailed rules:

- Exactly `5h = 10%` does not trigger a recovery-time message.
- Earned reset counts stay hidden while 7-day quota is still above zero.
- Recovery times must come from the server and still be in the future. Unknown, expired, or stale data does not produce an estimated time.
- After a Tibo countdown expires, the widget shows `Tibo 待确认` (awaiting confirmation). If 5-hour quota is exhausted and has a reliable recovery time, that time takes precedence.
- `额度已恢复` is a neutral result and does not attribute the recovery to Tibo. It appears only when the earned reset count falls while quota rises substantially, or when quota rises within a valid Tibo announcement window.
- A single recovery is announced once. Routine refreshes do not keep extending the five-minute message, and new exhaustion dismisses it immediately.

## Task-state rules

| Logo state | Meaning |
| --- | --- |
| Still, no badge | The widget has just started, or no new task has been observed |
| Spinning | At least one Codex task is running |
| Green check | All observed parallel tasks completed normally |
| Amber dot | Codex explicitly entered a waiting-for-input state |
| Gray cross | A task was cancelled, interrupted, or explicitly terminated |

The widget derives these states from local Codex session events. At startup it reconciles events from the current Codex process and restores unfinished tasks. Old completion records do not create a green check on startup. A state that Codex does not write to its session events cannot be detected reliably.

## Refresh and troubleshooting

- A Codex session event schedules a refresh after an approximately three-second debounce.
- While Codex is active, quota has a 30-second fallback refresh interval; while idle, it refreshes every 60 seconds.
- After an announced Tibo time is reached, quota is checked every 15 seconds for recovery.
- The public Tibo signal is checked every five minutes.
- Temporary failures retry after 10, 20, 40, and up to 60 seconds.
- The most recent quota, recovery timestamps, and reset count are cached under `%LOCALAPPDATA%\KleeCodexQuotaWidget` to avoid flashing empty values at startup.
- The right-click menu shows the latest refresh time and connection state.

If the widget keeps showing `--`, open Codex and confirm that you are signed in and can see Usage there, then double-click the Logo. Your network or proxy must also allow Codex to connect.

## Data and privacy

The app has no telemetry or project-owned server and does not upload workspace files or conversation text. See [PRIVACY.md](PRIVACY.md) for the complete description.

- Normal quota reads use the current user's local Codex login to request the official ChatGPT quota endpoint.
- When reset-credit details are needed, or the direct request fails, the widget uses the local `codex app-server --stdio` read-only `account/rateLimits/read` method.
- The access token is passed to the system `curl.exe` process through standard input. It is not placed in command-line arguments, logs, cache, Releases, or repository content.
- Task status reads local session event types and does not upload session content.
- Tibo announcements come from the independent community site [Codex Reset Monitor](https://codexreset.org/), which is not affiliated with OpenAI.

## Build from source

Building requires Windows 11, an installed Codex App, and the .NET Framework C# compiler included with Windows. The build script reads an icon from the local official Codex installation and embeds it in the EXE; the icon file itself is not committed to this repository.

```powershell
.\build.ps1
```

Build, enable per-user startup, and run:

```powershell
.\build.ps1 -Install -Run
```

Run the built-in state and rendering verification:

```powershell
.\dist\KleeCodexQuotaWidget.exe --verify-status .\dist\verification
Get-Content .\dist\verification\result.txt
```

Verification covers task states, the priority matrix, 10% and 0% boundaries, tomorrow and expired timestamps, recovery deduplication, transparent alpha, light and dark colors, off-screen rendering at 100%/125%/150%/200%, and app-server response parsing. Version 1.3.0 was also checked on the current Windows 11 desktop for transparent display, Logo hit testing, text click-through, and dragging. Community testing is still welcome for additional physical-DPI, multi-monitor, and taskbar auto-hide combinations.

## Repository layout

```text
src/Program.cs          Application and built-in verification
src/app.manifest        Windows privilege, compatibility, and DPI declarations
packaging/              Install, uninstall, and usage files
docs/                   README screenshots
build.ps1               Local build script
PRIVACY.md              Data and privacy details
SECURITY.md             Private security reporting instructions
NOTICE.md               Third-party name and trademark notice
```

Taskbar placement and quota-reading ideas were informed by the MIT-licensed `upstream-ray/codex-usage-monitor` and `Tooblippe/codex-usage` projects. This repository is an independently written, lightweight WinForms implementation.

## Contributing

Issues and pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) before contributing. Report security problems privately according to [SECURITY.md](SECURITY.md).

Source code is available under the [MIT License](LICENSE). Third-party names, marks, and graphics are not included in that license.
