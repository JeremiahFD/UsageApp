# Architecture and privacy boundaries

UsageApp is a read-only monitor. It cannot redeem banked resets or mutate a
provider account.

## Native Windows beta

```text
Codex CLI app-server (private stdio) -> normalized local snapshot -> tray/flyout/dashboard

Claude Code status line -> tokenized loopback receiver -> normalized quota-only cache
```

### Codex

The app starts the official Codex CLI as a private child process, performs the
documented app-server handshake, and reads:

- `account/rateLimits/read` for quota windows, reset timestamps, plan, and
  banked-reset details;
- `account/usage/read` for optional account-level daily token activity; and
- `account/rateLimits/updated` as a signal to refresh.

Codex remains responsible for authentication. UsageApp never reads
`~/.codex/auth.json`, copies credentials, scrapes ChatGPT, or exposes the
app-server transport to a network. `availableCount` remains authoritative for
banked resets; missing expiry detail is displayed as unavailable, not invented.

The documented activity feed is separate from remaining quota. It currently
does not supply historical model/reasoning attribution, request counts, or
tokens per minute, so the native dashboard marks those dimensions unavailable.

### Claude Code

Claude monitoring is off until the user explicitly connects it. UsageApp then
installs a reversible wrapper around Claude Code's documented status line. The
wrapper posts normalized quota fields to a random tokenized URL on a receiver
bound only to `127.0.0.1`. Payload size is limited, unknown fields are ignored,
and only quota/reset/freshness data is cached.

UsageApp preserves the prior Claude status-line command and restores it on
disconnect or uninstall. A new Claude Code session and prompt are required
after connecting because Claude Code reads its configuration at startup.

This native beta does not read Claude credentials, transcripts, browser
cookies, or Chrome sessions, and it does not provide Claude activity history.
Claude quota is marked stale when no current session refreshes it.

### Local data

Settings and normalized last-known snapshots are stored under
`%LOCALAPPDATA%\UsageAppNative`. They contain no provider credentials or raw
provider responses. Uninstall leaves this user data in place unless the user
removes it manually.

## Windows surfaces

Windows does not support arbitrary live text inside a pinned taskbar button.
UsageApp uses notification-area icons rendered at multiple DPI sizes, a
taskbar-adjacent popup, and a separate dashboard window. The popup may dismiss
on outside click or remain always on top for the current session when pinned.

## Earlier source and Android

The repository retains the earlier Electron implementation and unfinished
Android companion for reference. Their broader analytics and phone-sync code
is not part of the current native Windows Beta 1 binary. No Android APK is
included in this release.
