# Verification record

## Windows 0.2.0 Beta 1

Verified on Windows 11 on 2026-07-30:

- `pnpm install --frozen-lockfile`
- `pnpm test`: 11 core tests and 47 Windows tests passed
- `pnpm typecheck`: core, Windows, and Android passed
- `pnpm build:windows`: production main, preload, and renderer bundles passed
- `pnpm package:windows`: x64 NSIS packaging passed
- Installed customizer check: the native red X showed the unsaved-change
  warning; Keep editing preserved the edit; Discard changes closed the window;
  and all 11 persisted icon fields matched the active saved preset afterward
- Public screenshots were rendered from the built interface using synthetic
  data and visually reviewed
- A privacy scan of source, screenshots, packaged ASAR, and installer found no
  personal home paths, private-repository names, email addresses, or common
  credential patterns

Release artifact:

- `UsageApp-0.2.0-beta.1-x64.exe`
- Size: `100,500,156` bytes
- SHA-256:
  `8CFA3BD654A18A5B9F36516CB02AA70F10891D04317B4F9C20B66E7532FF6FD3`

The locally installed Windows beta was started without restarting Windows.
Hands-on behavior can still vary with notification-area overflow settings,
display scaling, accessibility settings, and provider session state.

## Android source

`pnpm build:android` passed from the shorter development checkout after the
Android/core/contract source was content-compared with this public export.
Building from the longer temporary public-checkout path hit Windows' native
260-character filename limit.

That build proves the source can produce an APK. It does **not** prove physical
phone behavior. The Android companion remains unreleased until installation,
pairing, secure token storage, cached viewing, refresh, accessibility, and
revocation are tested on physical devices.

## Provider limits

- Codex history currently lacks historical model and reasoning-level
  attribution.
- Claude shared-plan percentages require an eligible live Claude Code
  status-line update.
- Claude detailed history is forward-looking local activity after connection,
  not complete past account history.
- The installer is not Authenticode-signed and may trigger SmartScreen.

## Previous 0.1.0 beta

The tag remains `v0.1.0`, but the GitHub release is labeled as the first public
beta. Its previously published Windows installer SHA-256 was:

`8D2955EC194019163AA84CFFA6FFF32CFF43169172B9B561DF8C310943967146`
