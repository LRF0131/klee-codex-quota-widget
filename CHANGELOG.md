# Changelog

## 1.2.0 - 2026-09-05

- Show `可重置 ×N` only when 7-day remaining quota is below 20% and the
  official account response reports earned reset credits.
- Confirm a redeemed reset only when the available count decreases and quota
  rises substantially, then show `额度已恢复` for five minutes.
- Keep reset opportunities hidden when the count is zero, unknown, or stale.
- Preserve the display priority: recovered quota, Tibo countdown, reset count.
- Add portable install/uninstall scripts and a privacy-safe release package.

## 1.1.0 - 2026-09-05

- Add Codex task-state animation and completion, waiting, and interruption
  indicators.
- Prevent taskbar clicks from temporarily covering the widget.
- Add DPI, multi-monitor, theme, auto-hide, startup, and stale-data handling.
- Replace the Tibo attribution with the neutral `额度已恢复` label.
