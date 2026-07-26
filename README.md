# UsageApp

UsageApp is a read-only Windows tray monitor for Codex usage. It keeps quota
windows, remaining percentages, and exact reset times visible without needing
to keep a browser page open.

## Download

[Download UsageApp 0.1.0 for Windows x64](https://github.com/JeremiahFD/UsageApp/releases/download/v0.1.0/UsageApp-Windows-0.1.0-x64.exe)

- Windows 11, x64
- Installer size: approximately 100 MB
- SHA-256:
  `8D2955EC194019163AA84CFFA6FFF32CFF43169172B9B561DF8C310943967146`
- No Windows restart is required

The installer is not code-signed, so Windows SmartScreen may show an
unrecognized-app warning. Verify the SHA-256 value before running it.

## Version 0.1.0 features

- Percentage icon in the Windows notification area
- Click-open usage flyout above the taskbar
- Optional always-on-top compact corner widget
- Every quota window returned by Codex
- Remaining percentage plus exact and relative reset times
- Banked-reset counts and expiration details when Codex provides them
- Optional launch at Windows sign-in
- Read-only Android companion pairing on a trusted private LAN

## Requirements

- Windows 11 x64
- Codex desktop or the Codex CLI
- A signed-in Codex account

## Privacy

UsageApp talks to the documented local Codex app-server. It does not scrape the
ChatGPT website, call private ChatGPT backend endpoints, or open Codex
credential files. UsageApp has no analytics or advertising SDK.

Phone sharing is off by default. If enabled, it shares only a sanitized,
read-only usage snapshot with paired devices on the local network.

See [PRIVACY.md](PRIVACY.md) for the complete data-handling summary.

## Project status

Version 0.1.0 is the first public preview. This public repository currently
hosts release builds and user documentation. New analytics-dashboard and
Claude-provider development remains private until it is ready for a stable
public release.

Please use [GitHub Issues](https://github.com/JeremiahFD/UsageApp/issues) for
bugs and feature requests.
