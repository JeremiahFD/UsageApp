# UsageApp native Windows beta

This is the source for the lightweight Windows release. It is a C# Windows
Forms application compiled against the .NET Framework included with Windows;
it does not bundle Electron, Chromium, Node.js, Android, or font files.

## Build and verify

From this directory in Windows PowerShell:

```powershell
.\build.ps1
.\out\UsageApp.Native.exe --self-test-output .\out\self-tests.txt
.\out\UsageApp.Native.exe --layout-probe-output .\out\layout-probe.txt
.\out\UsageApp.Native.exe --picker-smoke-output .\out\picker-smoke.txt
.\out\UsageApp.Native.exe --interaction-smoke-output .\out\interaction-smoke.txt
```

The current candidate passes 38 self-tests plus the layout, picker, and
provider/navigation interaction probes.

Create the unsigned per-user installer and portable ZIP with:

```powershell
.\installer\New-UsageAppNativeInstaller.ps1 -NoLaunch
```

Generated files stay under `out/` and `release/` and are not committed.

## Provider boundaries

- Codex quota and optional account-level daily activity use the documented
  local Codex app-server protocol. UsageApp never reads `~/.codex/auth.json`.
- Claude monitoring is opt-in, experimental, and untested against a subscribed
  account. It uses only Claude Code's documented status-line output. A new
  Claude Code session and prompt are needed after connection.
- This native beta has no Claude history and never reads browser cookies or
  browser sessions.
- UsageApp is read-only. It does not redeem resets, change account limits, or
  treat activity history as quota remaining.

Always compare displayed data with the official provider source. This beta is
not an authoritative billing, quota, or availability record.
