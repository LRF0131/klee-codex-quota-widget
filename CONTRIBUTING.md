# Contributing

Issues and pull requests are welcome.

1. Fork the repository and create a focused branch.
2. Keep the widget local-only and dependency-light.
3. Never commit Codex tokens, account IDs, session files, logs, or local cache.
4. Build with `build.ps1` on Windows 11 with Codex installed.
5. Run `KleeCodexQuotaWidget.exe --verify-status <output-directory>` and confirm
   that `result.txt` begins with `PASS`.
6. Explain visible behavior changes and include a screenshot when relevant.

By contributing, you agree that your contribution is licensed under the MIT
License. Third-party names, marks, and assets remain subject to their owners'
terms and are not covered by the MIT License.
