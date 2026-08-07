# Verification record

## Native Windows 0.2.0 Beta 1 refresh

Verified on Windows 11 x64 on 2026-08-07:

- Native build completed with the Windows .NET Framework compiler
- 38/38 native self-tests passed
- Automated 100%/125% text, installed-font, flyout, settings, and dashboard
  layout probes passed
- Settings-picker and provider/navigation/refresh interaction probes passed
- Codex-only, Claude-only, and both-provider visibility states passed,
  including automatic tray-icon dependencies and hidden single-provider
  switchers
- A sanitized live Codex probe returned two quota windows, two banked-reset
  expiry rows, 47 daily activity buckets, and account profile highlights
- Upgrade installation over the existing native beta completed without a
  restart and preserved the separate Electron installation
- Installer scripts parse successfully and the installer, config, uninstaller,
  Start-menu shortcut, and Apps & Features registration were checked
- Public screenshots use synthetic demo data

Release artifacts:

- `UsageApp-0.2.0-beta.1-x64.exe`: 303,104 bytes
- `UsageApp-0.2.0-beta.1-portable-x64.zip`: about 127 KiB

Hands-on behavior can still vary with notification-area overflow settings,
display scaling, accessibility settings, taskbar position, security policy,
and provider session state. The installer is not Authenticode-signed and may
trigger SmartScreen. Windows 10 x64, mixed-DPI multi-monitor movement,
Narrator, high contrast, and live Claude subscription behavior remain
independent test gates.

## Provider limits

- Codex history currently lacks historical model and reasoning-level
  attribution, request counts, and tokens-per-minute data.
- Claude percentages require an eligible live Claude Code status-line update
  after connection, a new Claude Code session, and a prompt.
- Claude has not been independently tested against a subscribed account in
  this native beta, and no Claude history is provided.
- Always verify displayed values through the official provider source.

## Android source

The Android companion is unreleased. A generated APK would still require
installation, pairing, secure-token, cache, refresh, accessibility, and
revocation testing on physical Android devices before a production claim.

## Previous 0.1.0 beta

The tag remains `v0.1.0`, but the GitHub release is labeled as the first public
beta. Its previously published Windows installer SHA-256 was:

`8D2955EC194019163AA84CFFA6FFF32CFF43169172B9B561DF8C310943967146`
