import {
  describeCredits,
  formatRelativeTime,
  formatObservedTime,
  formatResetTime,
  formatTokenCount,
  summarizeForTray,
  type AppSettings,
  type BankedReset,
  type CreditSummary,
  type ProviderId,
  type TrayIconSavedPreset,
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
import type {
  DesktopState,
  PairingCodeInfo,
  ProviderDesktopState,
} from "../shared/desktop";
import { ClaudeResetHelp, needsResetTimes } from "./ClaudeResetHelp";
import { DashboardView } from "./DashboardView";
import {
  INTERFACE_FONT_OPTIONS,
  interfaceFontFamily,
} from "./interface-font";

type Tab = "usage" | "settings";
type AppView = "flyout" | "widget" | "dashboard" | "tray-icons";

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

const resetDateFormatter = new Intl.DateTimeFormat(undefined, {
  weekday: "short",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
  timeZoneName: "short",
});

function resetDate(iso: string | null): string {
  if (!iso) return "Time unavailable";
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? "Time unavailable"
    : resetDateFormatter.format(date);
}

function fullDate(iso: string | null): string {
  if (!iso) return "Time unavailable";
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? "Time unavailable"
    : fullDateFormatter.format(date);
}

function lastKnownUsage(iso: string | null): string {
  if (!iso) return "Last known usage unavailable";
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? "Last known usage unavailable"
    : `Last known usage ${formatObservedTime(iso)} · ${formatRelativeTime(iso)}`;
}

function percentageStyle(value: number): CSSProperties {
  return { "--percentage": `${Math.min(100, Math.max(0, value))}%` } as CSSProperties;
}

function currentView(): AppView {
  const view = new URLSearchParams(window.location.search).get("view");
  return view === "widget" || view === "dashboard" || view === "tray-icons" ? view : "flyout";
}

function LoadingView(): ReactNode {
  return (
    <div className="loading-view">
      <span className="spinner" aria-hidden="true" />
      <span>Connecting to usage providers…</span>
    </div>
  );
}

function CompactWidget({
  state,
}: {
  state: DesktopState | null;
}): ReactNode {
  const provider =
    state?.providers.find((item) => item.id === state.activeProviderId) ??
    state?.providers[0] ??
    null;
  const providers = state?.settings.widgetProviderDisplay === "both"
    ? state.providers
    : provider ? [provider] : [];
  const snapshot = provider?.snapshot ?? null;
  const summary = snapshot ? summarizeForTray(snapshot) : null;
  const percentage = summary?.percentage ?? null;

  return (
    <main
      className="widget-shell"
      data-provider={provider?.id === "anthropic-claude" ? "claude" : "codex"}
      data-multi={providers.length > 1 ? "true" : "false"}
      style={{
        zoom: (state?.settings.widgetTextScale ?? 125) / 100,
        fontFamily: interfaceFontFamily(state?.settings.interfaceFont ?? "system"),
      }}
    >
      <div className="widget-drag-region">
        <div className="brand-mark mini">U</div>
        <div>
          <div className="widget-title">{providers.length > 1 ? "AI usage" : `${provider?.name ?? "AI"} usage`}</div>
          <div className="status-dot live">
            {providers.length > 1 ? "Codex + Claude" : "Compact readout"}
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
        title="Open full usage dashboard"
        onClick={() => {
          void window.usageApp.showDashboard();
        }}
      >
        {providers.length > 1 ? (
          <span className="widget-provider-list">
            {providers.map((item) => {
              const itemSummary = item.snapshot ? summarizeForTray(item.snapshot) : null;
              return (
                <span className="widget-provider-row" key={item.id}>
                  <strong>{item.name}</strong>
                  <b>{itemSummary?.percentage === null || !itemSummary ? "â€”" : `${Math.round(itemSummary.percentage)}%`}</b>
                  <span className="widget-provider-meta">
                    <small>{itemSummary?.nextResetAt ? `Reset ${formatRelativeTime(itemSummary.nextResetAt)}` : item.id === "anthropic-claude" ? "Start Claude Code" : "Reset time unavailable"}</small>
                    <small className="last-known">{lastKnownUsage(item.snapshot?.observedAt ?? null)}</small>
                  </span>
                </span>
              );
            })}
          </span>
        ) : null}
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
          <small className="last-known">{lastKnownUsage(snapshot?.observedAt ?? null)}</small>
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

function ProviderSwitch({
  activeProviderId,
  onChange,
}: {
  activeProviderId: ProviderId;
  onChange(providerId: ProviderId): void;
}): ReactNode {
  return (
    <div className="provider-switch" role="group" aria-label="Usage provider">
      <button
        className={activeProviderId === "openai-codex" ? "active" : ""}
        type="button"
        aria-pressed={activeProviderId === "openai-codex"}
        onClick={() => onChange("openai-codex")}
      >
        <span className="provider-glyph codex" aria-hidden="true">
          C
        </span>
        Codex
      </button>
      <button
        className={activeProviderId === "anthropic-claude" ? "active" : ""}
        type="button"
        aria-pressed={activeProviderId === "anthropic-claude"}
        onClick={() => onChange("anthropic-claude")}
      >
        <span className="provider-glyph claude" aria-hidden="true">
          A
        </span>
        Claude
      </button>
    </div>
  );
}

function UsageWindowCard({
  windowItem,
  providerName,
  observedAt,
}: {
  windowItem: UsageWindow;
  providerName: string;
  observedAt: string;
}): ReactNode {
  return (
    <article className="usage-window-card">
      <div className="card-row">
        <div>
          <div className="eyebrow">
            {providerName} limit
          </div>
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
            : `${providerName} did not provide a reset time.`}
        </span>
      </div>
      <small className="usage-observed">{lastKnownUsage(observedAt)}</small>
    </article>
  );
}

/**
 * Compact credit readout.
 *
 * Codex reports only a balance, an availability flag, and a spend-control
 * flag, so this stays a single row and lets "None" recede rather than
 * dressing an empty state up as a statistic.
 */
function CreditsRow({ credits }: { credits: CreditSummary | null }): ReactNode {
  const display = describeCredits(credits);
  return (
    <section className={`credits-row tone-${display.tone}`}>
      <span className="credits-label">Credits</span>
      <span className="credits-value">
        <strong>{display.headline}</strong>
        {display.detail ? <small>{display.detail}</small> : null}
      </span>
    </section>
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
  provider,
  onRefresh,
}: {
  state: DesktopState;
  provider: ProviderDesktopState;
  onRefresh(): Promise<void>;
}): ReactNode {
  const snapshot = provider.snapshot;
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
            <h2>{provider.name}</h2>
            <StatusBadge
              snapshot={snapshot}
              refreshing={provider.refreshPhase === "refreshing"}
            />
          </div>
          <p>
            {snapshot?.planType
              ? `${snapshot.planType} plan`
              : provider.id === "anthropic-claude"
                ? "Shared Claude plan limits"
                : "Current account limits"}
          </p>
          <div className="hero-reset">
            {summary?.nextResetAt ? (
              <>
                <strong>Next reset {formatResetTime(summary.nextResetAt).toLowerCase()}</strong>
                <span>{resetDate(summary.nextResetAt)}</span>
              </>
            ) : (
              <strong>Reset time unavailable</strong>
            )}
            <small className="hero-observed">{lastKnownUsage(snapshot?.observedAt ?? null)}</small>
          </div>
        </div>
      </section>

      {provider.id === "openai-codex" &&
      snapshot?.status === "auth-required" ? (
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

      {provider.lastError ? (
        <details className="error-details">
          <summary>Connection details</summary>
          <p>{provider.lastError}</p>
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
            disabled={provider.refreshPhase === "refreshing"}
            onClick={() => void onRefresh()}
          >
            {provider.refreshPhase === "refreshing" ? "Refreshing…" : "Refresh"}
          </button>
        </div>
        <div className="usage-window-list">
          {snapshot?.windows.length ? (
            snapshot.windows.map((windowItem) => (
              <UsageWindowCard
                key={windowItem.id}
                windowItem={windowItem}
                providerName={provider.name}
                observedAt={snapshot.observedAt}
              />
            ))
          ) : (
            <div className="empty-state">
              No usage windows are available yet.
            </div>
          )}
        </div>
      </section>

      {provider.id === "openai-codex" ? (
      <section className="section-block">
        <div className="section-heading">
          <div>
            <span className="eyebrow">Extra capacity</span>
            <h2>Banked resets</h2>
          </div>
          <div className={`banked-count ${bankedCount ? "has-resets" : ""}`}>
            {bankedCount === null ? "—" : bankedCount}
            <span>available</span>
          </div>
        </div>
        {snapshot?.bankedResets.items.length ? (
          <details className="banked-details">
            <summary>
              {snapshot.bankedResets.items.length === 1
                ? "Show expiry date"
                : "Show expiry dates"}
            </summary>
            <ul className="banked-list">
              {snapshot.bankedResets.items.map((reset) => (
                <BankedResetRow key={reset.id} reset={reset} />
              ))}
            </ul>
          </details>
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
      ) : null}

      {provider.id === "anthropic-claude" && needsResetTimes(snapshot) ? (
        <ClaudeResetHelp />
      ) : null}

      {provider.id === "openai-codex" ? (
        <CreditsRow credits={snapshot?.credits ?? null} />
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
          ? lastKnownUsage(snapshot.observedAt)
          : `Waiting for the first ${provider.name} update.`}
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

function TrayIconSettingsView({
  state,
  onUpdate,
}: {
  state: DesktopState;
  onUpdate(patch: Partial<AppSettings>): Promise<void>;
}): ReactNode {
  const settings = state.settings;
  const [presetName, setPresetName] = useState("");
  const presets: Array<{ id: AppSettings["trayIconPreset"]; title: string }> = [
    { id: "classic", title: "Original meter" },
    { id: "solid-percent", title: "Filled number" },
    { id: "badge", title: "Outlined number" },
    { id: "high-contrast", title: "High contrast" },
    { id: "colored-text", title: "Colored text only" },
  ];
  const activeSavedPreset = settings.trayIconSavedPresets.find(
    (preset) => preset.id === settings.trayIconActiveSavedPresetId,
  );
  const presetSelection = activeSavedPreset
    ? `saved:${activeSavedPreset.id}`
    : `builtin:${settings.trayIconPreset === "custom" ? "classic" : settings.trayIconPreset}`;

  useEffect(() => {
    setPresetName(activeSavedPreset?.name ?? "");
  }, [activeSavedPreset?.id, activeSavedPreset?.name]);

  const presetValues = (preset: AppSettings["trayIconPreset"]): Partial<AppSettings> => preset === "classic"
      ? { trayIconPreset: preset, trayIconShape: "circle", trayIconContent: "percent", trayIconFill: "dark", trayIconBorder: "thick", trayIconTextTone: "light", trayIconMaximizeText: false }
      : preset === "solid-percent"
        ? { trayIconPreset: preset, trayIconShape: "rounded-square", trayIconContent: "percent", trayIconFill: "solid", trayIconBorder: "none", trayIconTextTone: "auto", trayIconMaximizeText: true }
        : preset === "badge"
          ? { trayIconPreset: preset, trayIconShape: "circle", trayIconContent: "percent", trayIconFill: "dark", trayIconBorder: "thick", trayIconTextTone: "light", trayIconMaximizeText: false }
          : preset === "colored-text"
            ? {
                trayIconPreset: preset,
                trayIconShape: "circle",
                trayIconContent: "percent",
                trayIconFill: "transparent",
                trayIconBorder: "none",
                trayIconTextTone: "provider",
                trayIconCodexTextColor: "#38bdf8",
                trayIconClaudeTextColor: "#e89a62",
                trayIconMaximizeText: true,
              }
            : { trayIconPreset: "high-contrast", trayIconShape: "rounded-square", trayIconContent: "percent", trayIconFill: "dark", trayIconBorder: "thick", trayIconTextTone: "light", trayIconMaximizeText: true };
  const applySavedPreset = (id: string): void => {
    const saved = settings.trayIconSavedPresets.find((preset) => preset.id === id);
    if (!saved) return;
    void onUpdate({
      trayIconPreset: "custom",
      trayIconShape: saved.shape,
      trayIconContent: saved.content,
      trayIconFill: saved.fill,
      trayIconBorder: saved.border,
      trayIconCodexColor: saved.codexColor,
      trayIconClaudeColor: saved.claudeColor,
      trayIconTextTone: saved.textTone,
      trayIconCodexTextColor: saved.codexTextColor,
      trayIconClaudeTextColor: saved.claudeTextColor,
      trayIconMaximizeText: saved.maximizeText,
      trayIconFont: saved.font,
      trayIconActiveSavedPresetId: saved.id,
    });
  };
  const currentPresetValues = (
    id: string,
    name: string,
    patch: Partial<AppSettings> = {},
  ): TrayIconSavedPreset => ({
    id,
    name,
    shape: patch.trayIconShape ?? settings.trayIconShape,
    content: patch.trayIconContent ?? settings.trayIconContent,
    fill: patch.trayIconFill ?? settings.trayIconFill,
    border: patch.trayIconBorder ?? settings.trayIconBorder,
    codexColor: patch.trayIconCodexColor ?? settings.trayIconCodexColor,
    claudeColor: patch.trayIconClaudeColor ?? settings.trayIconClaudeColor,
    textTone: patch.trayIconTextTone ?? settings.trayIconTextTone,
    codexTextColor: patch.trayIconCodexTextColor ?? settings.trayIconCodexTextColor,
    claudeTextColor: patch.trayIconClaudeTextColor ?? settings.trayIconClaudeTextColor,
    maximizeText: patch.trayIconMaximizeText ?? settings.trayIconMaximizeText,
    font: patch.trayIconFont ?? settings.trayIconFont,
  });

  const nextPresetName = (): string => {
    const existingNames = new Set(
      settings.trayIconSavedPresets.map((preset) => preset.name.toLowerCase()),
    );
    let number = 1;
    while (existingNames.has(`new preset ${number}`)) number += 1;
    return `New Preset ${number}`;
  };
  const createNewPreset = (patch: Partial<AppSettings> = {}): void => {
    if (settings.trayIconSavedPresets.length >= 12) return;
    const name = nextPresetName();
    const id = `preset-${Date.now().toString(36)}`;
    void onUpdate({
      trayIconPreset: "custom",
      ...patch,
      trayIconSavedPresets: [...settings.trayIconSavedPresets, currentPresetValues(id, name, patch)],
      trayIconActiveSavedPresetId: id,
    });
  };
  const customize = (patch: Partial<AppSettings>): void => {
    if (!activeSavedPreset) {
      createNewPreset(patch);
      return;
    }
    void onUpdate({ trayIconPreset: "custom", ...patch });
  };
  const hasUnsavedChanges = activeSavedPreset !== undefined && (
    presetName.trim() !== activeSavedPreset.name ||
    settings.trayIconShape !== activeSavedPreset.shape ||
    settings.trayIconContent !== activeSavedPreset.content ||
    settings.trayIconFill !== activeSavedPreset.fill ||
    settings.trayIconBorder !== activeSavedPreset.border ||
    settings.trayIconCodexColor !== activeSavedPreset.codexColor ||
    settings.trayIconClaudeColor !== activeSavedPreset.claudeColor ||
    settings.trayIconTextTone !== activeSavedPreset.textTone ||
    settings.trayIconCodexTextColor !== activeSavedPreset.codexTextColor ||
    settings.trayIconClaudeTextColor !== activeSavedPreset.claudeTextColor ||
    settings.trayIconMaximizeText !== activeSavedPreset.maximizeText ||
    settings.trayIconFont !== activeSavedPreset.font
  );
  const confirmDiscardChanges = (
    message = "Exit without saving your preset changes?",
  ): boolean => !hasUnsavedChanges || window.confirm(message);

  useEffect(() => {
    const handleBeforeUnload = (event: BeforeUnloadEvent): void => {
      if (confirmDiscardChanges()) return;
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", handleBeforeUnload);
    return () => window.removeEventListener("beforeunload", handleBeforeUnload);
  }, [hasUnsavedChanges]);

  const applyPreset = (preset: AppSettings["trayIconPreset"]): void => {
    if (!confirmDiscardChanges("Switch presets and discard your unsaved changes?")) return;
    void onUpdate({ ...presetValues(preset), trayIconActiveSavedPresetId: null });
  };
  const selectSavedPreset = (id: string): void => {
    if (id === activeSavedPreset?.id || !confirmDiscardChanges()) return;
    applySavedPreset(id);
  };
  const updateSavedPreset = (): void => {
    if (!activeSavedPreset) return;
    const name = presetName.trim() || activeSavedPreset.name;
    void onUpdate({
      trayIconSavedPresets: settings.trayIconSavedPresets.map((preset) =>
        preset.id === activeSavedPreset.id ? currentPresetValues(preset.id, name) : preset,
      ),
    });
  };
  const deleteSavedPreset = (): void => {
    if (!activeSavedPreset) return;
    void onUpdate({
      ...presetValues("classic"),
      trayIconSavedPresets: settings.trayIconSavedPresets.filter((preset) => preset.id !== activeSavedPreset.id),
      trayIconActiveSavedPresetId: null,
    });
  };
  const previewStyle = {
    "--codex-color": settings.trayIconCodexColor,
    "--claude-color": settings.trayIconClaudeColor,
    "--codex-text-color": settings.trayIconCodexTextColor,
    "--claude-text-color": settings.trayIconClaudeTextColor,
  } as CSSProperties;
  const previewClasses = `${settings.trayIconShape} ${settings.trayIconFill} border-${settings.trayIconBorder} text-${settings.trayIconTextTone} font-${settings.trayIconFont} ${settings.trayIconMaximizeText || settings.trayIconBorder === "none" ? "maximize" : ""}`;

  return (
    <main
      className="tray-icons-page"
      style={{ fontFamily: interfaceFontFamily(settings.interfaceFont) }}
    >
      <header>
        <span className="eyebrow">Windows notification area</span>
        <h1>Tray icon settings</h1>
        <p>Start with one simple preset, then open customization only if you want to change it.</p>
      </header>
      <section className="preset-picker" aria-label="Tray icon presets">
        <label htmlFor="tray-preset">Preset</label>
        <div className="preset-picker-controls">
          <select
            id="tray-preset"
            value={presetSelection}
            onChange={(event) => {
              const value = event.currentTarget.value;
              if (value.startsWith("builtin:")) applyPreset(value.slice(8) as AppSettings["trayIconPreset"]);
              else if (value.startsWith("saved:")) selectSavedPreset(value.slice(6));
            }}
          >
            <optgroup label="Built in">
              {presets.map((preset) => <option key={preset.id} value={`builtin:${preset.id}`}>{preset.title}</option>)}
            </optgroup>
            {settings.trayIconSavedPresets.length > 0 && (
              <optgroup label="My presets">
                {settings.trayIconSavedPresets.map((preset) => <option key={preset.id} value={`saved:${preset.id}`}>{preset.name}</option>)}
              </optgroup>
            )}
          </select>
          <button
            type="button"
            disabled={settings.trayIconSavedPresets.length >= 12}
            onClick={() => createNewPreset()}
          >
            + New
          </button>
        </div>
        <span className="preset-picker-hint">New presets are named automatically and can be renamed below. “Colored text only” uses the largest available blue and orange numbers.</span>
      </section>
      <details className="tray-icon-custom" open={settings.trayIconPreset === "custom"}>
        <summary><strong>Customize selected preset</strong><span>Shape, fill, border, colors, and text</span></summary>
        <div className="tray-live-preview" style={previewStyle}>
          <span className={`custom-icon ${previewClasses}`}><b>{settings.trayIconContent === "provider" ? "O" : "90"}</b><small>Codex</small></span>
          <span className={`custom-icon claude ${previewClasses}`}><b>{settings.trayIconContent === "provider" ? "C" : "?"}</b><small>Claude</small></span>
        </div>
        <div className="tray-custom-grid">
          <label>Shape<select value={settings.trayIconShape} onChange={(event) => customize({ trayIconShape: event.currentTarget.value as "circle" | "rounded-square" })}><option value="circle">Circle</option><option value="rounded-square">Rounded square</option></select></label>
          <label>Fill<select value={settings.trayIconFill} onChange={(event) => customize({ trayIconFill: event.currentTarget.value as "solid" | "dark" | "transparent" })}><option value="solid">Provider color</option><option value="dark">Dark</option><option value="transparent">No fill</option></select></label>
          <label>Border<select value={settings.trayIconBorder} onChange={(event) => customize({ trayIconBorder: event.currentTarget.value as "none" | "thin" | "thick" })}><option value="none">None</option><option value="thin">Thin</option><option value="thick">Thick</option></select></label>
          <label>Center<select value={settings.trayIconContent} onChange={(event) => customize({ trayIconContent: event.currentTarget.value as "percent" | "provider" })}><option value="percent">Numbers only</option><option value="provider">Provider initial (O = OpenAI, C = Claude)</option></select></label>
          <label>Number style<select value={settings.trayIconFont} onChange={(event) => customize({ trayIconFont: event.currentTarget.value as AppSettings["trayIconFont"] })}><option value="classic">Classic (compact)</option><option value="bold">Bold (most legible)</option><option value="rounded">Rounded (lighter)</option></select></label>
          <label>Text color<select value={settings.trayIconTextTone} onChange={(event) => customize({ trayIconTextTone: event.currentTarget.value as AppSettings["trayIconTextTone"] })}><option value="auto">Automatic contrast</option><option value="provider">Match provider color</option><option value="custom">Custom colors</option><option value="dark">Dark text</option><option value="light">Light text</option></select></label>
        </div>
        <Toggle
          id="maximize-tray-text"
          checked={settings.trayIconMaximizeText}
          title="Fill the icon with text"
          description="Make the number as large as possible. Icons without a border do this automatically."
          onChange={(trayIconMaximizeText) => customize({ trayIconMaximizeText })}
        />
        <div className="tray-colors">
          <strong>Icon colors</strong>
          <div className="palette-buttons">
            <button type="button" onClick={() => customize({ trayIconCodexColor: "#38bdf8", trayIconClaudeColor: "#e89a62" })}>UsageApp</button>
            <button type="button" onClick={() => customize({ trayIconCodexColor: "#0072b2", trayIconClaudeColor: "#e69f00" })}>Color-safe</button>
            <button type="button" onClick={() => customize({ trayIconCodexColor: "#00a6ff", trayIconClaudeColor: "#ff6a00" })}>Vivid</button>
          </div>
          <div className="color-pickers">
            <label><span>Codex fill / border</span><input type="color" value={settings.trayIconCodexColor} onChange={(event) => customize({ trayIconCodexColor: event.currentTarget.value })} /></label>
            <label><span>Claude fill / border</span><input type="color" value={settings.trayIconClaudeColor} onChange={(event) => customize({ trayIconClaudeColor: event.currentTarget.value })} /></label>
            <label><span>Codex text</span><input type="color" value={settings.trayIconCodexTextColor} onChange={(event) => customize({ trayIconCodexTextColor: event.currentTarget.value, trayIconTextTone: "custom" })} /></label>
            <label><span>Claude text</span><input type="color" value={settings.trayIconClaudeTextColor} onChange={(event) => customize({ trayIconClaudeTextColor: event.currentTarget.value, trayIconTextTone: "custom" })} /></label>
          </div>
        </div>
        <div className="saved-preset-editor">
          <label htmlFor="tray-preset-name">Preset name<input id="tray-preset-name" maxLength={40} value={presetName} placeholder="My tray style" onChange={(event) => setPresetName(event.currentTarget.value)} /></label>
          <div>
            {activeSavedPreset ? (
              <>
                <button type="button" disabled={!hasUnsavedChanges} onClick={updateSavedPreset}>Save changes</button>
                <button type="button" className="danger" onClick={deleteSavedPreset}>Delete</button>
              </>
            ) : (
              <button type="button" onClick={() => createNewPreset()}>Create new preset</button>
            )}
          </div>
        </div>
      </details>
      <p className="tray-icon-note">For the clearest number on your display, try <strong>Colored text only</strong>. Exact reset details stay in the tooltip and flyout.</p>
    </main>
  );
}

function SettingsPanel({
  state,
  onUpdate,
  onPair,
  pairing,
  onRevoke,
  onConnectClaude,
  onDisconnectClaude,
  onOpenTrayIcons,
}: {
  state: DesktopState;
  onUpdate(patch: Partial<AppSettings>): Promise<void>;
  onPair(): Promise<void>;
  pairing: PairingCodeInfo | null;
  onRevoke(): Promise<void>;
  onConnectClaude(): Promise<void>;
  onDisconnectClaude(): Promise<void>;
  onOpenTrayIcons(): void;
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
          <label className="field-row" htmlFor="interface-font">
            <span>
              <strong>Interface font</strong>
              <small>Used by the flyout, widget, dashboard, and settings. Tray numbers have their own selector.</small>
            </span>
            <select
              id="interface-font"
              value={settings.interfaceFont}
              onChange={(event) =>
                void onUpdate({
                  interfaceFont: event.currentTarget.value as AppSettings["interfaceFont"],
                })
              }
            >
              {INTERFACE_FONT_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <div className="text-scale-setting">
            <div>
              <strong>Text sizes</strong>
              <small>Flyout, full dashboard, and compact widget.</small>
            </div>
            <div className="text-scale-grid">
              {([
                ["Flyout", "flyoutTextScale"],
                ["Dashboard", "dashboardTextScale"],
                ["Widget", "widgetTextScale"],
              ] as const).map(([label, key]) => (
                <label key={key}>
                  <span>{label}</span>
                  <select
                    value={settings[key]}
                    onChange={(event) => void onUpdate({
                      [key]: Number(event.currentTarget.value) as 100 | 125 | 150 | 175,
                    })}
                  >
                    <option value={100}>100%</option>
                    <option value={125}>125%</option>
                    <option value={150}>150%</option>
                    <option value={175}>175%</option>
                  </select>
                </label>
              ))}
            </div>
          </div>
          <label className="field-row" htmlFor="tray-provider-display">
            <span><strong>Taskbar icons</strong><small>Use an icon for the active provider, or separate Codex and Claude icons.</small></span>
            <select id="tray-provider-display" value={settings.trayProviderDisplay} onChange={(event) => void onUpdate({ trayProviderDisplay: event.currentTarget.value as "active" | "both" })}>
              <option value="active">Active provider</option>
              <option value="both">Codex and Claude</option>
            </select>
          </label>
          <div className="field-row">
            <span><strong>Tray icon appearance</strong><small>Choose a readable preset or customize its shape and center content.</small></span>
            <button className="secondary-button" type="button" onClick={onOpenTrayIcons}>Customize</button>
          </div>
          <label className="field-row" htmlFor="widget-provider-display">
            <span><strong>Compact widget</strong><small>Show one active provider or both providers together.</small></span>
            <select id="widget-provider-display" value={settings.widgetProviderDisplay} onChange={(event) => void onUpdate({ widgetProviderDisplay: event.currentTarget.value as "active" | "both" })}>
              <option value="active">Active provider</option>
              <option value="both">Codex and Claude</option>
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
        <span className="eyebrow">Anthropic Claude</span>
        <h2>Claude monitoring</h2>
        <div className="settings-card padded claude-connect-card">
          <div className="card-row wrap">
            <div>
              <strong>
                {state.claudeIntegration.state === "connected"
                  ? "Claude is connected"
                  : state.claudeIntegration.state === "awaiting-session"
                    ? "Claude setup is ready"
                  : state.claudeIntegration.state === "partial"
                    ? "Claude is partly connected"
                    : "Connect Claude Code"}
              </strong>
              <small>
                Shared plan limits plus local model, effort, token, and cost
                history.
              </small>
            </div>
            <span
              className={`status-badge ${
                state.claudeIntegration.state === "connected"
                  ? "live"
                  : state.claudeIntegration.state === "awaiting-session"
                    ? "stale"
                  : state.claudeIntegration.state === "partial"
                    ? "stale"
                    : "unavailable"
              }`}
            >
              <span className="status-indicator" />
              {state.claudeIntegration.state === "connected"
                ? "Connected"
                : state.claudeIntegration.state === "awaiting-session"
                  ? "Ready"
                  : state.claudeIntegration.state === "partial"
                  ? "Partial"
                  : "Off"}
            </span>
          </div>
          {state.claudeIntegration.message ? (
            <p className="settings-note">
              {state.claudeIntegration.message}
            </p>
          ) : null}
          <div className="button-row">
            {settings.claudeEnabled ? (
              <button
                className="secondary-button"
                type="button"
                onClick={() => void onDisconnectClaude()}
              >
                Disconnect Claude
              </button>
            ) : (
              <button
                className="primary-button claude-button"
                type="button"
                onClick={() => void onConnectClaude()}
              >
                Connect Claude
              </button>
            )}
          </div>
          <p className="privacy-note">
            UsageApp uses Claude Code&apos;s documented status-line and
            OpenTelemetry feeds. It stores only usage numbers and never saves
            prompts, responses, tool details, credentials, or account IDs.
            Detailed history begins after connection.
          </p>
          {state.claudeIntegration.state === "awaiting-session" ? (
            <p className="settings-note">No browser or Windows prompt is expected. Close any running Claude Code session, start a new one, and run a prompt so it can send the first status update.</p>
          ) : null}
        </div>
      </section>

      <section className="section-block settings-section">
        <span className="eyebrow">Notifications</span>
        <h2>Usage warnings</h2>
        <div className="settings-card padded">
          <Toggle id="usage-alerts" checked={settings.usageAlertsEnabled} title="Warn before a limit runs low" description="Windows notifications fire once as a live quota window crosses a chosen remaining percentage." onChange={(usageAlertsEnabled) => void onUpdate({ usageAlertsEnabled })} />
          <label className="field-row" htmlFor="usage-alert-thresholds">
            <span><strong>Remaining-percent warnings</strong><small>Comma-separated values from 1 to 99. Presets: 50, 25, 10 or 25, 10.</small></span>
            <input id="usage-alert-thresholds" value={settings.usageAlertThresholds.join(", ")} onChange={(event) => { const thresholds = event.currentTarget.value.split(",").map((value) => Number(value.trim())).filter((value) => Number.isInteger(value) && value >= 1 && value <= 99); void onUpdate({ usageAlertThresholds: thresholds }); }} />
          </label>
          <div className="button-row">
            <button className="secondary-button" type="button" onClick={() => void onUpdate({ usageAlertThresholds: [25, 10] })}>25%, 10%</button>
            <button className="secondary-button" type="button" onClick={() => void onUpdate({ usageAlertThresholds: [50, 25, 10] })}>50%, 25%, 10%</button>
          </div>
        </div>
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

  if (view === "tray-icons") {
    return <TrayIconSettingsView state={state} onUpdate={update} />;
  }

  const activeProvider =
    state.providers.find((provider) => provider.id === state.activeProviderId) ??
    state.providers[0];

  if (!activeProvider) {
    return (
      <main className="app-shell">
        <div className="loading-view error">No usage providers are available.</div>
      </main>
    );
  }

  if (view === "dashboard") {
    return (
      <DashboardView
        state={state}
        actionError={actionError}
        onProviderChange={(activeProviderId) =>
          update({ activeProviderId })
        }
        onRefresh={() =>
          runAction(() => window.usageApp.refresh())
        }
        onConnectClaude={() =>
          runAction(() => window.usageApp.connectClaude())
        }
        onDisconnectClaude={() =>
          runAction(() => window.usageApp.disconnectClaude())
        }
        onUpdateSettings={update}
      />
    );
  }

  return (
    <main
      className="app-shell"
      data-provider={
        activeProvider.id === "anthropic-claude" ? "claude" : "codex"
      }
      style={{
        zoom: state.settings.flyoutTextScale / 100,
        fontFamily: interfaceFontFamily(state.settings.interfaceFont),
      }}
    >
      <header className="app-header">
        <div className="brand-mark">U</div>
        <div className="brand-copy">
          <strong>UsageApp</strong>
          <span>{activeProvider.name} monitor</span>
        </div>
        <button
          className="dashboard-button no-drag"
          type="button"
          title="Open full dashboard"
          onClick={() => void window.usageApp.showDashboard()}
        >
          Dashboard
        </button>
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

      <div className="provider-switch-row">
        <ProviderSwitch
          activeProviderId={state.activeProviderId}
          onChange={(activeProviderId) => {
            void update({ activeProviderId });
          }}
        />
      </div>

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
            provider={activeProvider}
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
            onConnectClaude={() =>
              runAction(() => window.usageApp.connectClaude())
            }
            onDisconnectClaude={() =>
              runAction(() => window.usageApp.disconnectClaude())
            }
            onOpenTrayIcons={() => void window.usageApp.showTrayIconSettings()}
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
