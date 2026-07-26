# UsageApp roadmap

This document describes possible post-0.1 work. Everything below is
**unreleased, in development, and subject to change**. It is not a commitment
to a particular design or release date.

## Planned 0.2 direction

- Add a resizable, maximizable full-screen usage dashboard alongside the
  existing tray flyout and compact widget.
- Add Today, 7-day, 30-day, 90-day, and custom-date activity views with daily
  token graphs, summary KPIs, and a detailed table.
- Keep model and reasoning controls capability-aware. The documented Codex
  account-history response currently supplies daily token totals but not
  historical model or reasoning-level dimensions, so those controls would
  remain disabled for Codex unless the official data source changes.
- Add an explicit opt-in Claude Code provider using Claude Code's documented
  status-line input and a local OpenTelemetry receiver bound only to loopback.
- Add a provider switch with distinct Codex and Claude themes.
- For newly captured local Claude Code activity, show model, effort,
  input/output/cache token categories, requests, and reported cost when those
  fields are emitted.
- Preserve the relevant existing Claude settings before connecting and restore
  them when disconnecting.
- Never collect prompts, responses, credentials, access tokens, account IDs, or
  unrelated telemetry fields.
- Keep the Android companion Codex-only initially.
- Package and publish an Android APK after physical-device validation of
  pairing, secure token storage, cached viewing, refresh, and revocation.

Claude detail collection would be forward-looking: history would begin only
after the user opts in and starts a new Claude Code session. It would describe
locally observed Claude Code activity, not claim to be complete account-wide
history. Claude effort can be used as a grouping dimension, but it would not be
presented as an exact hidden reasoning-token count.
