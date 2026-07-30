# UsageApp roadmap

UsageApp remains beta. This roadmap is directional, not a release commitment.

## Stabilize the Windows beta

- Test readability and tray rendering across Windows scaling and accessibility
  settings.
- Improve Claude live-limit diagnostics across eligible Claude Code plans and
  session states.
- Add clearer stale-data and provider-health explanations.
- Expand automated and hands-on testing for notifications, saved tray presets,
  multiple usage windows, and long banked-reset lists.
- Pursue Windows code signing when practical.

## Android companion

- Finish the standalone cached viewer experience.
- Validate pairing, encrypted token storage, refresh, cache expiry, and
  revocation on physical Android devices.
- Keep the phone contract normalized, read-only, and separate from provider
  credentials or local app-server transport.
- Publish an APK only after those physical-device checks pass.

## Data integrity

- Keep model and reasoning filters capability-aware.
- Never present activity totals as remaining quota.
- Never infer missing plan percentages or banked-reset expiry details.
- Continue documenting which metrics are account-wide and which are only
  locally observed after connection.
