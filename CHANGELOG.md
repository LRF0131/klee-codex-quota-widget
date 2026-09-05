# Changelog

## 1.4.0 - 2026-09-05

- Add a live 50%–200% UI scale slider with keyboard control, reset and saved preferences.
- Render text directly at the target resolution instead of enlarging a low-resolution bitmap.
- Fit scaling to taskbar bounds and scale the Logo input area consistently.
- Verify 30 DPI/custom-scale combinations, clipping and unchanged text during animation.

## 1.3.0 - 2026-09-05

- Add future recovery times for 5h below 10% and exhausted weekly quota.
- Show reset credits only at 7d = 0%; prioritize weekly exhaustion over Tibo.
- Prevent unchanged quota from triggering recovery and prevent repeated extension.
- Replace the opaque rectangle with a per-pixel alpha display and grayscale text.
- Move drag, double-click refresh and context menu to a separate Logo hit region.
- Cache recovery timestamps and add priority, alpha and DPI rendering checks.

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
