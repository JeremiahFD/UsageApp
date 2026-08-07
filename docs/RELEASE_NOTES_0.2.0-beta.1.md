# UsageApp 0.2.0 Beta 1

The visible Beta 1 download was refreshed on 2026-08-07 with a lightweight
native Windows build, replacing the earlier roughly 100 MB Electron installer
without creating another public beta version.

## Included

- Native Windows taskbar-area number icons and provider-focused popup
- Codex quota windows, reset times, banked-reset expiries, and last-known times
- Dashboard with date ranges and available account-level daily token history
- Provider visibility, tray-number source, text size, font, color, edge,
  warning, refresh, pin, and startup settings
- Experimental Claude Code status-line quota integration
- Per-user installer and portable x64 ZIP

## Important limitations

- This is unsigned beta software and may trigger Windows SmartScreen.
- It has been tested on Windows 11 x64; other Windows configurations still need
  independent testing.
- Claude support is experimental and has not been independently tested against
  a subscribed account. Connect it, restart Claude Code, and submit a prompt
  before expecting a status-line update.
- The native beta does not provide Claude history and does not access browser
  sessions or cookies.
- Historical Codex model/reasoning attribution, request counts, and tokens per
  minute are not supplied by the documented feed and are not estimated.
- There is no updater. Installing a later build over this one performs a
  per-user upgrade.
- Uninstall removes the installed app, its shortcuts, and its own startup
  entry. Settings and cached last-known data remain in
  `%LOCALAPPDATA%\UsageAppNative` unless removed manually.
- The Android APK is still in development and is not included.
- Always verify usage and reset information through the official provider.
  UsageApp is not the ultimate source of truth.

UsageApp was created with AI through continuous hands-on feedback, testing,
and iteration by JeremiahFD, not from a single prompt. It is MIT-licensed.
