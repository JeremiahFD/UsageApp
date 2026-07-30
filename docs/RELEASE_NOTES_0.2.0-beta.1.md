# UsageApp 0.2.0 Beta 1

UsageApp is still beta software. Expect bugs, incomplete provider data, and
Windows-specific display differences.

This beta was refreshed in place on 2026-07-30. The download and version stay
at 0.2.0 Beta 1 so this small customization update does not create another
installer entry.

## What's new

- A dedicated taskbar-icon font selector, separate from the interface font
- Original pixel taskbar fonts plus Segoe UI, Verdana, Tahoma, Arial,
  Trebuchet MS, Georgia, and Consolas
- Codex and opt-in Claude Code monitoring in one Windows app
- Separate blue and orange provider tray icons with provider-focused clicks
- Full dashboard with date filters, graphs, tables, tokens/minute, and
  capability-aware model and effort filters
- Exact last-known timestamps on live usage values
- Scrollable quota windows and banked-reset details
- Custom percentage-warning notifications
- Editable tray presets and separate font controls for tiny tray numbers versus
  the rest of the interface
- Optional two-provider compact widget

## Important limitations

- The installer is unsigned and may trigger Windows SmartScreen.
- The Windows download is about 100 MB because it includes the Electron and
  Chromium runtime. The Android build and Windows font files are not bundled
  in it. See [the measured breakdown and future options](https://github.com/JeremiahFD/UsageApp/blob/main/docs/INSTALLER_SIZE.md).
- Codex account history does not currently include historical model or
  reasoning-level attribution.
- Claude plan percentages appear only after an eligible current Claude Code
  session sends a status-line update.
- Claude detailed history is locally observed and begins after connection.
- The Android APK is still in development and is not included.

## Verification

The release asset is accompanied by `SHA256SUMS.txt`. Compare its value before
running the installer.
