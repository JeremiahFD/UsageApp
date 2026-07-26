# Architecture

UsageApp 0.1 is a read-only Codex usage monitor. Provider collection runs only
on the Windows PC, and no application path can redeem resets or otherwise
mutate an account.

```text
Codex app-server (private stdio)
              |
              v
       Windows collector
          |         |
          v         v
 tray/flyout/widget UsageSnapshot v1
                         |
                         v
             paired read-only LAN endpoint
                         |
                         v
               Android Codex viewer
```

## Codex provider

The desktop process starts the official Codex CLI as a private child process
using JSONL over standard input and output. It performs the documented
`initialize` handshake and reads:

- `account/rateLimits/read` for quota windows, reset timestamps, plan and
  credits, and banked-reset details;
- `account/usage/read` for optional token activity history;
- `account/rateLimits/updated` as a signal to perform another complete read.

Codex remains responsible for sign-in and token refresh. UsageApp never opens
or parses Codex credential files. The app-server transport is never bound to a
network interface.

The protocol is evolving, so the adapter accepts nullable fields, discovers all
returned limit IDs, derives labels from each window's duration, and normalizes
the result into the versioned `UsageSnapshot` contract. `availableCount` is
authoritative for banked resets; a missing detail list never becomes an
invented count.

`account/usage/read` is activity history, not remaining quota. Version 0.1
shows the summary values provided by Codex but does not infer unsupported
historical dimensions.

## Windows surface

Windows does not support arbitrary live text inside a pinned taskbar button.
UsageApp uses the supported notification area:

- a dynamically rendered percentage icon and tooltip;
- a click-open flyout anchored above the taskbar;
- an optional always-on-top compact corner widget;
- a reversible per-user launch-at-login setting.

## Phone sync

Phone sync is disabled by default. When enabled, the desktop starts a small
HTTP server and displays a short-lived six-digit pairing code. Successful
pairing returns a random per-device bearer token; Windows stores only its
SHA-256 hash. Android stores its token with Expo Secure Store, backed by Android
Keystore.

After pairing, a token can call only `GET /v1/snapshot` and revoke itself with
`DELETE /v1/device`. Neither endpoint can run Codex, redeem resets, access
files, or return credentials. The initial version uses authenticated HTTP on a
trusted private LAN, so it must not be used on public Wi-Fi or exposed through
router port forwarding.

The LAN API exposes only the normalized Codex `UsageSnapshot` v1 contract.
