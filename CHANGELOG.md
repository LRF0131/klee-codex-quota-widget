# Changelog

## 1.5.0 - 2026-09-06

- Add a persistent notification-area icon while the widget is running.
- Show current 5h/7d quota and task state in the tray tooltip.
- Open the existing menu from the tray icon and refresh on double-click.
- Create a desktop restart shortcut during installation and remove it during uninstall.

## 1.4.2 - 2026-09-06

- Fix clipped scale percentage, reset button and hint at 175% DPI.
- Measure embedded controls using the actual font; relayout on menu opening and monitor DPI changes.
- Scale slider drawing and hit coordinates together; wrap controls when space is limited.
- Fit menu bounds to the monitor work area and long action labels to narrow screens.
- Add 128 menu layout cases across DPI, text size, theme and width, plus input and DPI round-trip checks.
- Self-test failures now write a report and return a nonzero exit code instead of showing an exception dialog.

## 1.4.1 - 2026-09-05

- Embed a live scale slider and reset button directly in the right-click menu.
- Add theme-aware menu colors, rounded hover highlights and grouped controls.
- Preserve the menu while adjusting scale; save when it closes.

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
