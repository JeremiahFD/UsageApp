# Privacy

UsageApp is designed as a local, read-only usage monitor.

## Codex data

The Windows application starts the documented Codex app-server as a local
child process and communicates with it over private standard input and output.
Codex remains responsible for account authentication and network access.

UsageApp does not:

- read or copy Codex authentication files;
- scrape the ChatGPT or Codex user interface;
- call undocumented ChatGPT account endpoints;
- collect prompts, responses, or project files;
- include advertising, analytics, or tracking SDKs.

The application stores its display settings and last successful sanitized
usage snapshot locally on the computer.

## Optional phone sharing

Phone sharing is disabled by default. When explicitly enabled, UsageApp starts
a local HTTP endpoint for paired devices on the same network. It exposes only
the versioned, read-only usage snapshot.

Pairing uses a short-lived code and per-device random token. The Windows
application stores only a SHA-256 hash of each token. This initial LAN feature
does not use TLS, so it should be enabled only on a trusted private network and
must not be forwarded through a router.

## Crash reports and telemetry

UsageApp 0.1.0 does not send UsageApp crash reports, analytics, or usage
telemetry to the developer.
