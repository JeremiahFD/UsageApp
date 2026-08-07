# Changelog

## 0.2.0-beta.1 - 2026-07-30

This is a public beta release. Bugs and provider-data limitations are expected.

- Refreshed the visible Beta 1 download on 2026-08-07 with the lightweight
  native Windows build. The 296 KiB installer replaces the
  previous approximately 100 MB Electron installer without adding another
  public beta entry.
- Added provider visibility rules, smoother in-place settings updates,
  focus-dismiss and always-on-top pinning, precision touchpad scrolling,
  responsive text sizing, more fonts, and a native dashboard.
- Added a user-selectable taskbar-number source: lowest remaining, shortest
  reset window, or longest reset window.
- Added an explicit official-source disclaimer, untested Claude disclosure,
  and AI-development disclosure.

### Earlier Electron implementation

- Refreshed this beta in place on 2026-07-30 with a dedicated taskbar-icon
  font selector. The original pixel fonts remain available, alongside Segoe
  UI, Verdana, Tahoma, Arial, Trebuchet MS, Georgia, and Consolas.
- Added a native warning when closing the taskbar preset window with unsaved
  changes. Discarding restores the last saved preset instead of leaving the
  unsaved live preview active.
- Added opt-in Claude Code quota and local activity monitoring to the earlier
  Electron implementation. The current native beta exposes Claude status-line
  quota only.
- Added a full dashboard with date ranges, graphs, tables, summary cards,
  tokens/minute, and provider-aware model and effort filters.
- Added separate blue Codex and orange Claude tray icons that open the matching
  provider.
- Added exact last-known timestamps beside live usage values.
- Added a scrollable view for all quota windows and banked resets.
- Added customizable percentage-warning notifications.
- Added editable tray presets with fill, border, taskbar font, text color, and
  provider color controls.
- Added an independent interface font selector plus separate text sizes for the
  flyout, dashboard, and compact widget.
- Added two-provider tray-icon and compact-widget modes.
- Improved Claude reset guidance and preserved honest unavailable states until
  Claude Code emits an eligible live update.

## 0.1.0-beta (tagged v0.1.0) - 2026-07-26

This was the first public beta, although its original release title did not say
beta.

- Added the Windows notification-area usage indicator.
- Added quota windows, reset dates and times, and banked-reset details.
- Added the click-open flyout and optional compact desktop widget.
- Added launch-at-sign-in and refresh settings.
- Added optional read-only Android pairing on a trusted local network.
