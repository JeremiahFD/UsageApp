# Installer size and future options

## Why Beta 1 is about 100 MB

UsageApp 0.2.0 Beta 1 is an Electron application. Electron makes the interface
consistent and lets the project share TypeScript and React code, but each
Windows installer includes its own Chromium, Node.js, graphics, media,
internationalization, and Electron runtime.

Measured from the Beta 1 Windows package:

| Component | Approximate size |
| --- | ---: |
| Compiled UsageApp code and styles | 2.14 MB |
| Electron executable, uncompressed | 215.17 MB |
| Chromium language packs, uncompressed | 46.65 MB |
| Complete installed bundle, uncompressed | 348.20 MB |
| Previous compressed Windows installer | 99,786,851 bytes |
| Refreshed Beta 1 installer | 100,500,156 bytes |

The taskbar-font refresh and unsaved-change close warning increased the
compressed installer by 713,305 bytes (about 696.6 KiB). They did not cause the
approximately 100 MB baseline size.

The Android build is separate and is not included in the Windows installer.
Taskbar font files are not bundled either: the taskbar selector uses fonts
already installed by Windows, with UsageApp's small pixel fonts as fallbacks.

## Options for a smaller future build

### 1. Trim the existing Electron package

- Package only the language resources the app officially supports.
- Audit optional Electron resources and build settings.
- Compare compression settings and remove only files proven unnecessary.

This is the lowest-risk path and can reduce some overhead, but Chromium and
Electron will still dominate the download. It will not turn the current app
into a genuinely tiny installer.

### 2. Evaluate Tauri with the Windows WebView2 runtime

Tauri can use the WebView2 runtime already present on supported Windows
systems instead of packaging a full Chromium copy with UsageApp. Much of the
React interface could remain, while the Electron main process, tray handling,
local providers, notifications, settings, and installer integration would need
to be ported and re-tested.

This is the leading candidate for a substantially smaller download, but it is
an architectural migration rather than a packaging switch. A proof of concept
should verify tray readability, dual-provider icons, startup behavior, Codex
stdio, Claude integrations, notifications, accessibility scaling, upgrades,
and uninstall behavior before the public build changes.

### 3. Rebuild the Windows interface with native UI

WinUI, WPF, or another native Windows stack could avoid Electron and offer the
deepest Windows integration. It would also require the largest rewrite and
could reduce code sharing with the Android companion.

## Proposed decision path

1. Measure an English-only Electron package without replacing the current
   public beta.
2. Build a small Tauri proof of concept for one Codex tray icon and flyout.
3. Compare download size, memory use, startup time, accessibility, behavior at
   different display scales, and maintenance cost.
4. Migrate only if the proof of concept preserves UsageApp's provider and
   privacy boundaries and materially improves the user experience.

No smaller-size target is promised until those builds are measured and the
Windows behavior is verified.
