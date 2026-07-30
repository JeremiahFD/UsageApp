# Architecture

UsageApp deliberately keeps provider collection and analytics on the Windows
PC. It is a read-only monitor: none of its provider paths can redeem resets or
otherwise mutate an account.

```text
Codex app-server (private stdio)       Claude Code
              |                 status line + OTLP/HTTP
              v                         |
       Codex provider                   v
              |                 Claude provider
              +------------+------------+
                           |
                           v
              provider-aware desktop state
                 |                    |
                 v                    v
        tray/flyout/widget     full-screen dashboard
                 |
                 v
      Codex UsageSnapshot v1 only
                 |
                 v
       paired read-only LAN endpoint
                 |
                 v
          Android Codex viewer
```

## Codex provider

The desktop process starts the official Codex CLI as a private child process
using JSONL over stdio. It performs the documented `initialize` handshake and
reads:

- `account/rateLimits/read` for quota windows, reset timestamps, plan and
  credits, and banked reset details;
- `account/usage/read` for optional token activity history;
- `account/rateLimits/updated` as a signal to perform another complete read.

Codex remains responsible for sign-in and token refresh. UsageApp never opens
or parses Codex credential files. The app-server transport is never bound to a
network interface.

The protocol is documented but still evolving. The adapter therefore accepts
nullable fields, discovers all returned limit IDs, derives labels from each
window's duration, and stores a versioned provider-neutral snapshot.

`account/usage/read` is activity history, not remaining quota. Its current
documented account response supplies daily token totals and lifetime summary
values, but no historical model, reasoning-level, token-category, request, or
cost dimensions. The desktop capability model records that distinction so the
dashboard can disable unsupported Codex filters with an explanation instead of
inventing data.

## Claude Code provider

Claude monitoring is off until the user explicitly connects it. UsageApp then
configures two documented Claude Code extension points:

- the status-line input for live shared-plan quota percentages, reset
  timestamps, current model, and current-session context;
- OpenTelemetry logs delivered over OTLP/HTTP to a receiver bound only to
  loopback for detailed local Claude Code activity.

Only sanitized numeric and categorical activity fields needed for the
dashboard are retained: timestamps, model, effort, input/output/cache token
counts, request counts, and reported cost when Claude emits them. Prompts,
responses, credentials, access tokens, account IDs, and unrelated event
content are neither stored nor forwarded. Unknown fields are not promoted into
the analytics store.

Connecting first preserves the relevant existing Claude settings and
status-line configuration. Disconnecting restores that preserved
configuration rather than replacing it with a generic default. UsageApp does
not require a Windows restart, but Claude Code reads this configuration at
process startup, so a new Claude Code session is required after connecting.

OpenTelemetry collection is forward-looking. Detailed history begins when the
integration is connected and UsageApp is running; UsageApp does not read
Claude transcripts or credential files to backfill older activity. Shared
Claude quota can cover more than the local Claude Code process, so quota and
locally observed activity are modeled and labeled separately. Status-line
quota is marked stale when no current Claude session is refreshing it.

Claude exposes effort as an activity dimension. UsageApp can group ordinary
token counts by that effort value, but does not claim to know an exact hidden
reasoning-token count.

## Analytics capability model

The full-screen dashboard uses the same layout for both providers while
enabling only dimensions backed by provider data:

| Dimension | Codex | Claude Code |
| --- | --- | --- |
| Live quota and resets | App-server rate limits | Status-line updates |
| Daily token history | Account daily totals | Locally observed OTLP events |
| Model breakdown/filter | Not supplied | Available after connection |
| Reasoning or effort filter | Not supplied | Effort, when emitted |
| Input/output/cache categories | Not supplied | Available when emitted |
| Requests and reported cost | Not supplied | Available when emitted |

Date presets and custom day/range selection operate on the daily history that
each provider actually supplies. Unsupported controls remain visible but
disabled so absence of a dimension cannot be confused with a zero value.

## Windows surface

Windows does not support arbitrary live text inside a pinned taskbar button.
UsageApp uses the supported notification area instead:

- a dynamically rendered percentage icon and tooltip;
- a click-open flyout anchored above the taskbar;
- an optional always-on-top compact corner widget;
- a resizable, maximizable, full-screen analytics window with provider-themed
  colors;
- a reversible per-user launch-at-login setting.

## Phone sync

Phone sync is disabled by default. When enabled, the desktop starts a small
HTTP server and shows a short-lived six-digit pairing code. Successful pairing
returns a per-device random bearer token; the desktop stores only its SHA-256
hash. The Android app stores the token through Android Keystore via Expo Secure
Store.

After pairing, a token can call only `GET /v1/snapshot` and revoke itself with
`DELETE /v1/device`. Neither endpoint can run Codex, redeem resets, access
files, or return credentials. The initial MVP uses HTTP on the trusted private
LAN, so it should not be enabled on public Wi-Fi and must never be exposed
through router port forwarding. A later remote-access version should add
authenticated encryption or an end-to-end encrypted relay.

The Android endpoint deliberately remains Codex `UsageSnapshot` v1. The
provider-aware desktop analytics state, including Claude data, is not exposed
to the LAN and does not alter the Android contract.
