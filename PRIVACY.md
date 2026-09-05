# Privacy

Klee Codex Quota Widget runs locally and has no analytics, telemetry, or
project-owned server.

## Data read locally

- Codex authentication state from the current user's `CODEX_HOME` (or
  `%USERPROFILE%\.codex`).
- Codex session event files, only to derive running, waiting, completed, and
  interrupted task states.
- Windows taskbar, display, theme, proxy, and startup settings.

## Network requests

- ChatGPT's quota endpoint, using the current local Codex login.
- The local `codex app-server` read-only `account/rateLimits/read` method when
  fallback data or earned reset counts are needed.
- `https://codexreset.org/` every five minutes for explicit public Tibo reset
  announcements.

The access token is passed to the system `curl.exe` process through standard
input. It is not included in process arguments, logs, cache files, releases, or
repository content.

The local cache contains quota percentages, refresh time, and available reset
count. It does not contain authentication tokens or conversation text.
