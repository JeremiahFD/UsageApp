import {
  formatRelativeTime,
  formatResetTime,
  formatTokenCount,
  summarizeForTray,
  type AppSettings,
  type BankedReset,
  type UsageSnapshot,
  type UsageWindow,
} from "@usageapp/core";
import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode,
} from "react";
import type { DesktopState, PairingCodeInfo } from "../shared/desktop";

type Tab = "usage" | "settings";
type AppView = "flyout" | "widget";

const numberFormatter = new Intl.NumberFormat();
const fullDateFormatter = new Intl.DateTimeFormat(undefined, {
  weekday: "short",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
  second: "2-digit",
  timeZoneName: "short",
});

function fullDate(iso: string | null): string {
  if (!iso) return "Time unavailable";
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? "Time unavailable"
    : fullDateFormatter.format(date);
}

function percentageStyle(value: number): CSSProperties {
  return { "--percentage": `${Math.min(100, Math.max(0, value))}%` } as CSSProperties;
}

function currentView(): AppView {
  const view = new URLSearchParams(window.location.search).get("view");
  return view === "widget" ? "widget" : "flyout";
}

function LoadingView(): ReactNode {
  return (
    <div className="loading-view">
      <span className="spinner" aria-hidden="true" />
      <span>Connecting to Codex…</span>
    </div>
  );
}

function CompactWidget({
  state,
}: {
  state: DesktopState | null;
}): ReactNode {
  const snapshot = state?.snapshot ?? null;
  const summary = snapshot ? summarizeForTray(snapshot) : null;
  const percentage = summary?.percentage ?? null;

  return (
    <main className="widget-shell">
      <div className="widget-drag-region">
        <div className="brand-mark mini">U</div>
        <div>
          <div className="widget-title">Codex usage</div>
          <div className={`status-dot ${snapshot?.status ?? "starting"}`}>
            {snapshot?.status === "live"
              ? "Live"
              : snapshot?.status === "stale"
                ? "Last known"
                : "Connecting"}
          </div>
        </div>
        <button
          className="icon-button no-drag"
          type="button"
          title="Hide compact widget"
          aria-label="Hide compact widget"
          onClick={() => {
            void window.usageApp.updateSettings({ showWidget: false });
          }}
        >
          ×
        </button>
      </div>
      <button
        className="widget-content no-drag"
        type="button"
        title="Open Codex usage"
        onClick={() => {
          void window.usageApp.showFlyout();
        }}
      >
        <span className="widget-percent">
          {percentage === null ? "—" : `${Math.round(percentage)}%`}
        </span>
        <span className="widget-copy">
          <strong>remaining</strong>
          <span>
            {summary?.nextResetAt
              ? `${formatResetTime(summary.nextResetAt)} · ${formatRelativeTime(summary.nextResetAt)}`
              : "Reset time unavailable"}
          </span>
        </span>
        <span className="widget-open" aria-hidden="true">
          ›
        </span>
      </button>
    </main>
  );
}

function StatusBadge({
  snapshot,
  refreshing,
}: {
  snapshot: UsageSnapshot | null;
  refreshing: boolean;
}): ReactNode {
  const label = refreshing
    ? "Refreshing"
    : snapshot?.status === "live"
      ? "Live"
      : snapshot?.status === "stale"
        ? "Last known"
        : snapshot?.status === "auth-required"
          ? "Sign-in needed"
          : "Unavailable";
  return (
    <span className={`status-badge ${snapshot?.status ?? "starting"}`}>
      <span className="status-indicator" />
      {label}
    </span>
  );
}

function UsageWindowCard({
  windowItem,
}: {
  windowItem: UsageWindow;
}): ReactNode {
  return (
    <article className="usage-window-card">
      <div className="card-row">
        <div>
          <div className="eyebrow">Codex limit</div>
          <h3>{windowItem.label}</h3>
        </div>
        <div className="window-percent">
          {Math.round(windowItem.remainingPercent)}%
          <span>left</span>
        </div>
      </div>
      <div
        className="progress-track"
        role="progressbar"
        aria-label={`${windowItem.label} remaining`}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(windowItem.remainingPercent)}
      >
        <span style={{ width: `${windowItem.remainingPercent}%` }} />
      </div>
      <div className="reset-copy">
        <strong>{formatResetTime(windowItem.resetsAt)}</strong>
        <span>
          {windowItem.resetsAt
            ? `${fullDate(windowItem.resetsAt)} · ${formatRelativeTime(windowItem.resetsAt)}`
            : "Codex did not provide a reset time."}
        </span>
      </div>
    </article>
  );
}

function BankedResetRow({ reset }: { reset: BankedReset }): ReactNode {
  return (
    <li className="banked-row">
      <div className="card-row">
        <strong>{reset.title ?? "Banked reset"}</strong>
        <span className="mini-pill">{reset.status}</span>
      </div>
      {reset.description ? <p>{reset.description}</p> : null}
      <div className="reset-copy">
        <strong>
          {reset.expiresAt
            ? `Expires ${formatResetTime(reset.expiresAt).toLowerCase()}`
            : "No expiration returned"}
        </strong>
        <span>
          {reset.expiresAt
            ? `${fullDate(reset.expiresAt)} · ${formatRelativeTime(reset.expiresAt)}`
            : `Granted ${fullDate(reset.grantedAt)}`}
        </span>
      </div>
    </li>
  );
}

function UsagePanel({
  state,
  onRefresh,
}: {
  state: DesktopState;
  onRefresh(): Promise<void>;
}): ReactNode {
  const snapshot = state.snapshot;
  const summary = snapshot ? summarizeForTray(snapshot) : null;
  const percentage = summary?.percentage ?? null;
  const bankedCount = snapshot?.bankedResets.availableCount ?? null;

  return (
    <div className="panel-content">
      <section className="hero-card">
        <div
          className={`usage-ring ${percentage === null ? "unknown" : ""}`}
          style={percentageStyle(percentage ?? 0)}
          aria-label={
            percentage === null
              ? "Remaining usage unavailable"
              : `${Math.round(percentage)} percent remaining`
          }
        >
          <div>
            <strong>
              {percentage === null ? "—" : `${Math.round(percentage)}%`}
            </strong>
            <span>remaining</span>
          </div>
        </div>
        <div className="hero-copy">
          <div className="card-row wrap">
            <h2>Codex</h2>
            <StatusBadge
              snapshot={snapshot}
              refreshing={state.refreshPhase === "refreshing"}
            />
          </div>
          <p>
            {snapshot?.planType
              ? `${snapshot.planType} plan`
              : "Current account limits"}
          </p>
          <div className="hero-reset">
            {summary?.nextResetAt ? (
              <>
                <strong>Next reset {formatResetTime(summary.nextResetAt).toLowerCase()}</strong>
                <span>{fullDate(summary.nextResetAt)}</span>
              </>
            ) : (
              <strong>Reset time unavailable</strong>
            )}
          </div>
        </div>
      </section>

      {snapshot?.status === "auth-required" ? (
        <section className="callout warning">
          <strong>Codex sign-in required</strong>
          <p>
            Open a terminal, run <code>codex login</code>, finish sign-in, then
            refresh here. UsageApp delegates authentication to Codex.
          </p>
        </section>
      ) : null}

      {snapshot?.message ? (
        <section className="callout">
          <p>{snapshot.message}</p>
        </section>
      ) : null}

      {state.lastError ? (
        <details className="error-details">
          <summary>Connection details</summary>
          <p>{state.lastError}</p>
        </details>
      ) : null}

      <section className="section-block">
        <div className="section-heading">
          <div>
            <span className="eyebrow">Rate limits</span>
            <h2>Usage windows</h2>
          </div>
          <button
            className="secondary-button"
            type="button"
            disabled={state.refreshPhase === "refreshing"}
            onClick={() => void onRefresh()}
          >
            {state.refreshPhase === "refreshing" ? "Refreshing…" : "Refresh"}
          </button>
        </div>
        <div className="usage-window-list">
          {snapshot?.windows.length ? (
            snapshot.windows.map((windowItem) => (
              <UsageWindowCard key={windowItem.id} windowItem={windowItem} />
            ))
          ) : (
            <div className="empty-state">
              No usage windows are available yet.
            </div>
          )}
        </div>
      </section>

      <section className="section-block">
        <div className="section-heading">
          <div>
            <span className="eyebrow">Extra capacity</span>
            <h2>Banked resets</h2>
          </div>
          <div className="banked-count">
            {bankedCount === null ? "—" : bankedCount}
            <span>available</span>
          </div>
        </div>
        {snapshot?.bankedResets.items.length ? (
          <ul className="banked-list">
            {snapshot.bankedResets.items.map((reset) => (
              <BankedResetRow key={reset.id} reset={reset} />
            ))}
          </ul>
        ) : (
          <div className="empty-state compact">
            {snapshot?.bankedResets.detailsAvailable
              ? "No banked resets are currently listed."
              : bankedCount === null
                ? "Codex did not provide an authoritative banked-reset count."
                : bankedCount > 0
                  ? "Banked resets are available, but expiration details were not returned."
                  : "No banked reset details were returned."}
          </div>
        )}
      </section>

      {snapshot?.credits ? (
        <section className="section-block">
          <span className="eyebrow">Credits</span>
          <div className="metric-grid">
            <div className="metric-card">
              <strong>
                {snapshot.credits.unlimited
                  ? "Unlimited"
                  : snapshot.credits.balance ?? "—"}
              </strong>
              <span>Credit balance</span>
            </div>
            <div className="metric-card">
              <strong>{snapshot.credits.hasCredits ? "Available" : "None"}</strong>
              <span>Credit status</span>
            </div>
          </div>
        </section>
      ) : null}

      {snapshot?.tokenUsage ? (
        <section className="section-block">
          <span className="eyebrow">History</span>
          <h2>Token activity</h2>
          <div className="metric-grid">
            <div className="metric-card">
              <strong>{formatTokenCount(snapshot.tokenUsage.lifetimeTokens)}</strong>
              <span>Lifetime tokens</span>
            </div>
            <div className="metric-card">
              <strong>{formatTokenCount(snapshot.tokenUsage.peakDailyTokens)}</strong>
              <span>Peak daily</span>
            </div>
            <div className="metric-card">
              <strong>
                {snapshot.tokenUsage.currentStreakDays === null
                  ? "—"
                  : numberFormatter.format(snapshot.tokenUsage.currentStreakDays)}
              </strong>
              <span>Day streak</span>
            </div>
            <div className="metric-card">
              <strong>
                {snapshot.tokenUsage.longestRunningTurnSec === null
                  ? "—"
                  : `${numberFormatter.format(Math.round(snapshot.tokenUsage.longestRunningTurnSec / 60))}m`}
              </strong>
              <span>Longest turn</span>
            </div>
          </div>
        </section>
      ) : null}

      <div className="updated-copy">
        {snapshot
          ? `Observed ${fullDate(snapshot.observedAt)} · ${formatRelativeTime(snapshot.observedAt)}`
          : "Waiting for the first Codex update."}
      </div>
    </div>
  );
}

function Toggle({
  id,
  checked,
  title,
  description,
  disabled = false,
  onChange,
}: {
  id: string;
  checked: boolean;
  title: string;
  description: string;
  disabled?: boolean;
  onChange(checked: boolean): void;
}): ReactNode {
  return (
    <label className={`setting-row ${disabled ? "disabled" : ""}`} htmlFor={id}>
      <span>
        <strong>{title}</strong>
        <small>{description}</small>
      </span>
      <input
        id={id}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.currentTarget.checked)}
      />
      <span className="toggle" aria-hidden="true" />
    </label>
  );
}

function SettingsPanel({
  state,
  onUpdate,
  onPair,
  pairing,
  onRevoke,
}: {
  state: DesktopState;
  onUpdate(patch: Partial<AppSettings>): Promise<void>;
  onPair(): Promise<void>;
  pairing: PairingCodeInfo | null;
  onRevoke(): Promise<void>;
}): ReactNode {
  const settings = state.settings;
  const [commandDraft, setCommandDraft] = useState(settings.codexCommand);
  const [portDraft, setPortDraft] = useState(String(settings.phoneSyncPort));

  useEffect(() => {
    setCommandDraft(settings.codexCommand);
  }, [settings.codexCommand]);
  useEffect(() => {
    setPortDraft(String(settings.phoneSyncPort));
  }, [settings.phoneSyncPort]);

  const saveCommand = () => {
    const value = commandDraft.trim() || "auto";
    setCommandDraft(value);
    if (value !== settings.codexCommand) {
      void onUpdate({ codexCommand: value });
    }
  };

  const savePort = () => {
    const parsed = Number(portDraft);
    if (Number.isInteger(parsed) && parsed >= 1_024 && parsed <= 65_535) {
      if (parsed !== settings.phoneSyncPort) {
        void onUpdate({ phoneSyncPort: parsed });
      }
    } else {
      setPortDraft(String(settings.phoneSyncPort));
    }
  };

  return (
    <div className="panel-content settings-panel">
      <section className="section-block settings-section">
        <span className="eyebrow">Windows</span>
        <h2>Tray and desktop</h2>
        <div className="settings-card">
          <Toggle
            id="launch-at-login"
            checked={settings.launchAtLogin}
            title="Launch at login"
            description="Start UsageApp when you sign in to Windows."
            onChange={(launchAtLogin) => void onUpdate({ launchAtLogin })}
          />
          <Toggle
            id="show-widget"
            checked={settings.showWidget}
            title="Show compact widget"
            description="Keep a small always-on-top readout above the bottom-right taskbar."
            onChange={(showWidget) => void onUpdate({ showWidget })}
          />
          <Toggle
            id="start-minimized"
            checked={settings.startMinimized}
            title="Start in the tray"
            description="Do not open the flyout when UsageApp starts manually."
            onChange={(startMinimized) => void onUpdate({ startMinimized })}
          />
          <label className="field-row" htmlFor="refresh-interval">
            <span>
              <strong>Refresh interval</strong>
              <small>How often UsageApp asks the local Codex app-server.</small>
            </span>
            <select
              id="refresh-interval"
              value={settings.refreshIntervalMinutes}
              onChange={(event) =>
                void onUpdate({
                  refreshIntervalMinutes: Number(event.currentTarget.value),
                })
              }
            >
              {[1, 2, 5, 10, 15, 30, 60].map((minutes) => (
                <option key={minutes} value={minutes}>
                  {minutes} min
                </option>
              ))}
            </select>
          </label>
        </div>
        <p className="settings-note">
          The percentage icon lives in the Windows notification area. Windows
          controls whether an app is pinned directly on the taskbar or placed in
          its overflow menu.
        </p>
      </section>

      <section className="section-block settings-section">
        <span className="eyebrow">Codex connection</span>
        <h2>Local command</h2>
        <div className="settings-card padded">
          <label className="stacked-field" htmlFor="codex-command">
            <strong>Codex executable</strong>
            <small>
              Use <code>auto</code> to try the installed CLI, then the pinned
              official npm fallback.
            </small>
            <input
              id="codex-command"
              value={commandDraft}
              spellCheck={false}
              onChange={(event) => setCommandDraft(event.currentTarget.value)}
              onBlur={saveCommand}
              onKeyDown={(event) => {
                if (event.key === "Enter") {
                  event.currentTarget.blur();
                }
              }}
            />
          </label>
          <p className="privacy-note">
            UsageApp talks to <code>codex app-server</code> over local stdio.
            It never opens or copies Codex authentication files.
          </p>
        </div>
      </section>

      <section className="section-block settings-section">
        <span className="eyebrow">Android companion</span>
        <h2>Phone sync</h2>
        <div className="settings-card">
          <Toggle
            id="phone-sync"
            checked={settings.phoneSyncEnabled}
            title="Share on this LAN"
            description="Expose only the current read-only snapshot to paired phones."
            onChange={(phoneSyncEnabled) =>
              void onUpdate({ phoneSyncEnabled })
            }
          />
          <label className="field-row" htmlFor="phone-port">
            <span>
              <strong>Local port</strong>
              <small>Allowed range: 1024–65535</small>
            </span>
            <input
              id="phone-port"
              className="port-input"
              type="number"
              min={1_024}
              max={65_535}
              value={portDraft}
              disabled={!settings.phoneSyncEnabled}
              onChange={(event) => setPortDraft(event.currentTarget.value)}
              onBlur={savePort}
              onKeyDown={(event) => {
                if (event.key === "Enter") {
                  event.currentTarget.blur();
                }
              }}
            />
          </label>
        </div>

        {settings.phoneSyncEnabled ? (
          <div className="pairing-card">
            <div className="card-row wrap">
              <div>
                <strong>
                  {state.phoneSync.listening
                    ? "Ready on your network"
                    : "Phone server is not listening"}
                </strong>
                <small>
                  {state.phoneSync.pairedDeviceCount} paired{" "}
                  {state.phoneSync.pairedDeviceCount === 1
                    ? "device"
                    : "devices"}
                </small>
              </div>
              <span
                className={`status-badge ${
                  state.phoneSync.listening ? "live" : "unavailable"
                }`}
              >
                <span className="status-indicator" />
                {state.phoneSync.listening ? "On" : "Off"}
              </span>
            </div>

            {state.phoneSync.error ? (
              <p className="inline-error">{state.phoneSync.error}</p>
            ) : null}

            {pairing ? (
              <div className="pairing-code">
                <span>One-time pairing code</span>
                <strong>{pairing.code}</strong>
                <small>
                  Expires {formatResetTime(pairing.expiresAt).toLowerCase()} (
                  {formatRelativeTime(pairing.expiresAt)})
                </small>
              </div>
            ) : null}

            <div className="address-list">
              {(pairing?.addresses.length
                ? pairing.addresses
                : state.phoneSync.addresses
              ).map((address) => (
                <code key={address}>{address}</code>
              ))}
            </div>

            <div className="button-row">
              <button
                className="primary-button"
                type="button"
                disabled={!state.phoneSync.listening}
                onClick={() => void onPair()}
              >
                {pairing ? "Replace pairing code" : "Create pairing code"}
              </button>
              {state.phoneSync.pairedDeviceCount > 0 ? (
                <button
                  className="danger-button"
                  type="button"
                  onClick={() => void onRevoke()}
                >
                  Revoke phones
                </button>
              ) : null}
            </div>
            <p className="privacy-note">
              Use only on a trusted private network. LAN traffic is not
              encrypted. Tokens are stored only as hashes, and paired phones
              can only read a snapshot or revoke their own token; no
              reset-redemption action is exposed.
            </p>
          </div>
        ) : (
          <p className="settings-note">
            Phone sync is off by default. Turn it on only while your Windows PC
            and phone share a trusted network.
          </p>
        )}
      </section>

      <button
        className="quit-button"
        type="button"
        onClick={() => void window.usageApp.quit()}
      >
        Quit UsageApp
      </button>
    </div>
  );
}

export function App(): ReactNode {
  const [state, setState] = useState<DesktopState | null>(null);
  const [tab, setTab] = useState<Tab>("usage");
  const [pairing, setPairing] = useState<PairingCodeInfo | null>(null);
  const pairingDeviceCount = useRef<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const view = useMemo(currentView, []);

  useEffect(() => {
    let active = true;
    const unsubscribe = window.usageApp.onStateChanged((nextState) => {
      if (active) setState(nextState);
    });
    void window.usageApp
      .getState()
      .then((nextState) => {
        if (active) setState(nextState);
      })
      .catch((error: unknown) => {
        if (active) {
          setActionError(
            error instanceof Error ? error.message : "Could not load UsageApp.",
          );
        }
      });
    return () => {
      active = false;
      unsubscribe();
    };
  }, []);

  useEffect(() => {
    if (
      !state?.settings.phoneSyncEnabled ||
      !state.phoneSync.listening ||
      !state.phoneSync.pairingCodeActive ||
      (pairing &&
        pairingDeviceCount.current !== null &&
        pairingDeviceCount.current !== state.phoneSync.pairedDeviceCount)
    ) {
      setPairing(null);
      pairingDeviceCount.current = null;
    }
  }, [
    pairing,
    state?.phoneSync.listening,
    state?.phoneSync.pairedDeviceCount,
    state?.phoneSync.pairingCodeActive,
    state?.settings.phoneSyncEnabled,
    state?.settings.phoneSyncPort,
  ]);

  useEffect(() => {
    if (!pairing) return;
    const delay = Date.parse(pairing.expiresAt) - Date.now();
    if (!Number.isFinite(delay) || delay <= 0) {
      setPairing(null);
      pairingDeviceCount.current = null;
      return;
    }
    const timer = setTimeout(() => {
      setPairing(null);
      pairingDeviceCount.current = null;
    }, delay);
    return () => clearTimeout(timer);
  }, [pairing]);

  const runAction = useCallback(
    async (action: () => Promise<DesktopState>): Promise<void> => {
      setActionError(null);
      try {
        setState(await action());
      } catch (error) {
        setActionError(
          error instanceof Error ? error.message : "The action failed.",
        );
      }
    },
    [],
  );

  const update = useCallback(
    async (patch: Partial<AppSettings>): Promise<void> => {
      await runAction(() => window.usageApp.updateSettings(patch));
    },
    [runAction],
  );

  if (view === "widget") {
    return <CompactWidget state={state} />;
  }

  if (!state) {
    return (
      <main className="app-shell">
        {actionError ? (
          <div className="loading-view error">{actionError}</div>
        ) : (
          <LoadingView />
        )}
      </main>
    );
  }

  return (
    <main className="app-shell">
      <header className="app-header">
        <div className="brand-mark">U</div>
        <div className="brand-copy">
          <strong>UsageApp</strong>
          <span>Codex monitor</span>
        </div>
        <button
          className="icon-button no-drag"
          type="button"
          title="Close to tray"
          aria-label="Close to tray"
          onClick={() => void window.usageApp.hideFlyout()}
        >
          ×
        </button>
      </header>

      {actionError ? (
        <div className="action-error" role="alert">
          <span>{actionError}</span>
          <button type="button" onClick={() => setActionError(null)}>
            Dismiss
          </button>
        </div>
      ) : null}

      <div className="scroll-region">
        {tab === "usage" ? (
          <UsagePanel
            state={state}
            onRefresh={() => runAction(() => window.usageApp.refresh())}
          />
        ) : (
          <SettingsPanel
            state={state}
            pairing={pairing}
            onUpdate={update}
            onPair={async () => {
              setActionError(null);
              try {
                const nextPairing =
                  await window.usageApp.createPairingCode();
                pairingDeviceCount.current =
                  state.phoneSync.pairedDeviceCount;
                setPairing(nextPairing);
              } catch (error) {
                setActionError(
                  error instanceof Error
                    ? error.message
                    : "Could not create a pairing code.",
                );
              }
            }}
            onRevoke={async () => {
              if (
                window.confirm(
                  "Revoke every paired phone? Each phone will need a new pairing code.",
                )
              ) {
                setPairing(null);
                pairingDeviceCount.current = null;
                await runAction(() => window.usageApp.revokePhoneTokens());
              }
            }}
          />
        )}
      </div>

      <nav className="bottom-nav" aria-label="UsageApp sections">
        <button
          className={tab === "usage" ? "active" : ""}
          type="button"
          onClick={() => setTab("usage")}
        >
          <span aria-hidden="true">◔</span>
          Usage
        </button>
        <button
          className={tab === "settings" ? "active" : ""}
          type="button"
          onClick={() => setTab("settings")}
        >
          <span aria-hidden="true">⚙</span>
          Settings
        </button>
      </nav>
    </main>
  );
}
