# Privacy

UsageApp is a local, read-only usage monitor.

## Codex data

The Windows app starts the documented Codex app-server as a local child process
and communicates with it over private standard input and output. Codex remains
responsible for authentication and network access.

UsageApp does not read or copy Codex authentication files, scrape ChatGPT or
Codex pages, or call undocumented ChatGPT account endpoints.

## Claude data

Claude monitoring is off until the user connects it. UsageApp configures only
Claude Code's documented status-line and OpenTelemetry integrations. Its local
receiver binds to loopback.

UsageApp retains normalized quota windows and numeric activity fields such as
model, effort, token categories, duration, and reported cost. It does not save
prompts, responses, tool details, credentials, access tokens, account IDs, or
unrelated telemetry fields. Detailed history is forward-looking from the time
monitoring is enabled.

## Local storage

The app stores display settings, notification settings, sanitized usage
snapshots, and normalized local activity history on the computer. It does not
send UsageApp crash reports, developer analytics, or advertising telemetry.

## Optional phone sharing

Phone sharing is disabled by default. When explicitly enabled, UsageApp starts
a local HTTP endpoint for paired devices on the same network. It exposes only
the versioned, read-only Codex usage snapshot.

Pairing uses a short-lived code and per-device random token. The Windows app
stores only a SHA-256 hash of each token. This LAN feature does not use TLS, so
it should be enabled only on a trusted private network and must not be
forwarded through a router.
