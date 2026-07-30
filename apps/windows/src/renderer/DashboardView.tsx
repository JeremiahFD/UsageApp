import {
  describeCredits,
  formatRelativeTime,
  formatResetTime,
  type ProviderId,
  type UsageSnapshot,
} from "@usageapp/core";
import {
  useEffect,
  useMemo,
  useState,
  type CSSProperties,
  type ReactNode,
} from "react";
import type {
  DesktopState,
  ProviderDesktopState,
  UsageHistoryBucket,
} from "../shared/desktop";
import { ClaudeResetHelp, needsResetTimes } from "./ClaudeResetHelp";
import { interfaceFontFamily } from "./interface-font";
import {
  DATE_RANGE_PRESETS,
  getUsageFilterOptions,
  localDateKey,
  resolveDateRange,
  scaleChartBars,
  selectAndAggregateUsage,
  type AggregatedUsageDay,
  type DateRangePreset,
} from "./analytics";
import "./dashboard.css";

export interface DashboardViewProps {
  state: DesktopState;
  onProviderChange(providerId: ProviderId): void | Promise<void>;
  onRefresh(): void | Promise<void>;
  onConnectClaude?(): void | Promise<void>;
  onDisconnectClaude?(): void | Promise<void>;
  onUpdateSettings?(patch: Partial<DesktopState["settings"]>): void | Promise<void>;
  actionError?: string | null;
}

interface BreakdownItem {
  label: string;
  tokens: number;
}

const compactNumber = new Intl.NumberFormat(undefined, {
  notation: "compact",
  maximumFractionDigits: 1,
});
const exactNumber = new Intl.NumberFormat();
const currency = new Intl.NumberFormat(undefined, {
  style: "currency",
  currency: "USD",
  minimumFractionDigits: 2,
  maximumFractionDigits: 4,
});
const calendarDate = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});
const dateTime = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

function formatCount(value: number | null, exact = false): string {
  if (value === null || !Number.isFinite(value)) return "—";
  return (exact ? exactNumber : compactNumber).format(value);
}

function formatMoney(value: number | null): string {
  return value === null || !Number.isFinite(value)
    ? "—"
    : currency.format(value);
}

function formatDateKey(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return value;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  return calendarDate.format(new Date(year, month - 1, day));
}

function formatTimestamp(value: string | null): string {
  if (!value) return "Not recorded yet";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime())
    ? "Not recorded yet"
    : dateTime.format(parsed);
}

function statusLabel(snapshot: UsageSnapshot | null, awaitingClaude = false): string {
  if (awaitingClaude) return "Ready - start Claude";
  if (!snapshot) return "Waiting for data";
  if (snapshot.status === "auth-required") return "Sign-in required";
  if (snapshot.status === "unavailable") return "Unavailable";
  if (snapshot.status === "stale") return "Stale";
  return "Live";
}

function providerTheme(provider: ProviderDesktopState): "codex" | "claude" {
  return provider.id === "anthropic-claude" ? "claude" : "codex";
}

function providerGlyph(providerId: ProviderId): string {
  return providerId === "anthropic-claude" ? "C" : "O";
}

function aggregateDimension(
  buckets: readonly UsageHistoryBucket[],
  dimension: "model" | "reasoningLevel",
): BreakdownItem[] {
  const totals = new Map<string, number>();
  for (const bucket of buckets) {
    const label = bucket[dimension]?.trim() || "Unspecified";
    totals.set(label, (totals.get(label) ?? 0) + bucket.totalTokens);
  }
  return [...totals.entries()]
    .map(([label, tokens]) => ({ label, tokens }))
    .sort((left, right) => right.tokens - left.tokens);
}

function ProviderSelector({
  providers,
  activeProviderId,
  awaitingClaude,
  onChange,
}: {
  providers: readonly ProviderDesktopState[];
  activeProviderId: ProviderId;
  awaitingClaude: boolean;
  onChange(providerId: ProviderId): void | Promise<void>;
}): ReactNode {
  return (
    <div className="dash-provider-switch" aria-label="Usage provider">
      {providers.map((provider) => (
        <button
          key={provider.id}
          className={provider.id === activeProviderId ? "active" : ""}
          type="button"
          aria-pressed={provider.id === activeProviderId}
          onClick={() => void onChange(provider.id)}
        >
          <span className={`dash-provider-glyph ${providerTheme(provider)}`}>
            {providerGlyph(provider.id)}
          </span>
          <span>
            <strong>{provider.name}</strong>
            <small>{statusLabel(provider.snapshot, provider.id === "anthropic-claude" && awaitingClaude)}</small>
          </span>
        </button>
      ))}
    </div>
  );
}

function QuotaSection({
  provider,
}: {
  provider: ProviderDesktopState;
}): ReactNode {
  const snapshot = provider.snapshot;
  const isCodex = provider.id === "openai-codex";

  return (
    <section className="dash-section" aria-labelledby="quota-heading">
      <div className="dash-section-heading">
        <div>
          <span className="dash-eyebrow">Live allowance</span>
          <h2 id="quota-heading">Quota and resets</h2>
        </div>
        <div className="dash-heading-pills">
          {snapshot ? (
            <span className="dash-quota-observed">
              Last known usage {formatTimestamp(snapshot.observedAt)} · {formatRelativeTime(snapshot.observedAt)}
            </span>
          ) : null}
          {snapshot?.planType ? (
            <span className="dash-plan-pill">{snapshot.planType}</span>
          ) : null}
          <span
            className={`dash-status-pill ${snapshot?.status ?? "unavailable"}`}
          >
            <span />
            {statusLabel(snapshot)}
          </span>
        </div>
      </div>

      {snapshot?.windows.length ? (
        <div className="dash-quota-grid">
          {snapshot.windows.map((windowItem) => {
            const remaining = Math.min(
              100,
              Math.max(0, windowItem.remainingPercent),
            );
            return (
              <article className="dash-quota-card" key={windowItem.id}>
                <div className="dash-quota-card-top">
                  <div>
                    <span className="dash-card-kicker">
                      {windowItem.limitName ?? "Usage window"}
                    </span>
                    <h3>{windowItem.label}</h3>
                  </div>
                  <div className="dash-quota-value">
                    <strong>{Math.round(remaining)}%</strong>
                    <span>left</span>
                  </div>
                </div>
                <div
                  className="dash-quota-track"
                  role="progressbar"
                  aria-label={`${windowItem.label} remaining`}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={Math.round(remaining)}
                >
                  <span style={{ width: `${remaining}%` }} />
                </div>
                <div className="dash-reset-row">
                  <span>Resets</span>
                  <div>
                    <strong>{formatResetTime(windowItem.resetsAt)}</strong>
                    <small>
                      {windowItem.resetsAt
                        ? formatRelativeTime(windowItem.resetsAt)
                        : "Provider did not return a reset time"}
                    </small>
                  </div>
                </div>
                <small className="dash-card-observed">
                  Last known usage {formatTimestamp(snapshot.observedAt)} · {formatRelativeTime(snapshot.observedAt)}
                </small>
              </article>
            );
          })}

          {isCodex ? <CreditsCard snapshot={snapshot} /> : null}

          {isCodex && snapshot.bankedResets.availableCount !== null ? (
            <article className="dash-quota-card dash-banked-card">
              <div className="dash-quota-card-top">
                <div>
                  <span className="dash-card-kicker">Codex</span>
                  <h3>Banked resets</h3>
                </div>
                <div className="dash-quota-value">
                  <strong>{snapshot.bankedResets.availableCount}</strong>
                  <span>available</span>
                </div>
              </div>
              {snapshot.bankedResets.detailsAvailable &&
              snapshot.bankedResets.items.length > 0 ? (
                <div className="dash-expiry-list">
                  {snapshot.bankedResets.items.map((reset) => (
                    <div key={reset.id}>
                      <strong>{reset.title ?? "Banked reset"}</strong>
                      <small>
                        {reset.expiresAt
                          ? `Expires ${formatResetTime(
                              reset.expiresAt,
                            ).toLowerCase()} · ${formatRelativeTime(
                              reset.expiresAt,
                            )}`
                          : "No expiration returned"}
                      </small>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="dash-muted">
                  Expiration details were not returned.
                </p>
              )}
            </article>
          ) : null}
        </div>
      ) : (
        <div className="dash-empty-card">
          <strong>No live quota windows yet</strong>
          <p>
            {provider.lastError ??
              snapshot?.message ??
              (provider.id === "anthropic-claude"
                ? "Connect Claude monitoring, then start a new Claude Code session to receive shared plan limits."
                : "Refresh after Codex is signed in to load current limits.")}
          </p>
        </div>
      )}

      {!isCodex && needsResetTimes(snapshot) ? (
        <ClaudeResetHelp variant="dashboard" />
      ) : null}
    </section>
  );
}

/**
 * Credit balance card.
 *
 * Codex exposes a balance, an availability flag, and a spend-control flag and
 * nothing else, so this shows those three and states plainly that the rest is
 * not reported rather than leaving blank fields that look like a loading bug.
 */
function CreditsCard({
  snapshot,
}: {
  snapshot: UsageSnapshot;
}): ReactNode {
  const credits = snapshot.credits;
  const display = describeCredits(credits);

  return (
    <article
      className={`dash-quota-card dash-credits-card tone-${display.tone}`}
    >
      <div className="dash-quota-card-top">
        <div>
          <span className="dash-card-kicker">Codex</span>
          <h3>Credits</h3>
        </div>
        <div className="dash-quota-value">
          <strong>{display.headline}</strong>
          {display.detail ? <span>{display.detail}</span> : null}
        </div>
      </div>
      {credits === null ? (
        <p className="dash-muted">
          Codex did not report a credit balance for this account.
        </p>
      ) : (
        <div className="dash-credit-facts">
          <div>
            <span>Balance</span>
            <strong>
              {credits.unlimited ? "Unlimited" : credits.balance ?? "Not reported"}
            </strong>
          </div>
          <div>
            <span>Spending limit</span>
            <strong>
              {credits.spendControlReached === null
                ? "Not reported"
                : credits.spendControlReached
                  ? "Reached"
                  : "Not reached"}
            </strong>
          </div>
          {credits.rateLimitReachedType ? (
            <div>
              <span>Limit reached</span>
              <strong>{credits.rateLimitReachedType}</strong>
            </div>
          ) : null}
        </div>
      )}
      <p className="dash-fineprint">
        Codex does not report a credit limit, renewal date, or payment method,
        so those are not shown.
      </p>
    </article>
  );
}

function DateAndDimensionFilters({
  provider,
  preset,
  customStart,
  customEnd,
  selectedModel,
  selectedReasoning,
  onPresetChange,
  onCustomStartChange,
  onCustomEndChange,
  onModelChange,
  onReasoningChange,
}: {
  provider: ProviderDesktopState;
  preset: DateRangePreset;
  customStart: string;
  customEnd: string;
  selectedModel: string;
  selectedReasoning: string;
  onPresetChange(preset: DateRangePreset): void;
  onCustomStartChange(value: string): void;
  onCustomEndChange(value: string): void;
  onModelChange(value: string): void;
  onReasoningChange(value: string): void;
}): ReactNode {
  const capabilities = provider.analytics.capabilities;
  const options = getUsageFilterOptions(provider.analytics.buckets);
  const reasoningLabel =
    provider.id === "anthropic-claude" ? "Effort" : "Reasoning level";

  return (
    <section className="dash-filter-card" aria-label="Usage history filters">
      <div className="dash-date-filter">
        <span className="dash-filter-label">Date range</span>
        <div className="dash-date-presets">
          {DATE_RANGE_PRESETS.map((option) => (
            <button
              key={option.id}
              className={preset === option.id ? "active" : ""}
              type="button"
              aria-pressed={preset === option.id}
              onClick={() => onPresetChange(option.id)}
            >
              {option.label}
            </button>
          ))}
        </div>
        {preset === "custom" ? (
          <div className="dash-custom-dates">
            <label>
              <span>From</span>
              <input
                type="date"
                value={customStart}
                max={customEnd || undefined}
                onChange={(event) =>
                  onCustomStartChange(event.currentTarget.value)
                }
              />
            </label>
            <span aria-hidden="true">→</span>
            <label>
              <span>To</span>
              <input
                type="date"
                value={customEnd}
                min={customStart || undefined}
                onChange={(event) =>
                  onCustomEndChange(event.currentTarget.value)
                }
              />
            </label>
          </div>
        ) : null}
      </div>

      <div className="dash-dimension-filters">
        <label className={!capabilities.modelFilter ? "unavailable" : ""}>
          <span>Model</span>
          <select
            value={capabilities.modelFilter ? selectedModel : "unavailable"}
            disabled={!capabilities.modelFilter}
            onChange={(event) => onModelChange(event.currentTarget.value)}
          >
            {!capabilities.modelFilter ? (
              <option value="unavailable">Not supplied</option>
            ) : (
              <>
                <option value="all">All models</option>
                {options.models.map((model) => (
                  <option key={model} value={model}>
                    {model}
                  </option>
                ))}
              </>
            )}
          </select>
          {!capabilities.modelFilter ? (
            <small>Unavailable in this provider feed</small>
          ) : null}
        </label>

        <label className={!capabilities.reasoningFilter ? "unavailable" : ""}>
          <span>{reasoningLabel}</span>
          <select
            value={
              capabilities.reasoningFilter
                ? selectedReasoning
                : "unavailable"
            }
            disabled={!capabilities.reasoningFilter}
            onChange={(event) => onReasoningChange(event.currentTarget.value)}
          >
            {!capabilities.reasoningFilter ? (
              <option value="unavailable">Not supplied</option>
            ) : (
              <>
                <option value="all">
                  All {reasoningLabel.toLowerCase()} values
                </option>
                {options.reasoningLevels.map((level) => (
                  <option key={level} value={level}>
                    {level}
                  </option>
                ))}
              </>
            )}
          </select>
          {!capabilities.reasoningFilter ? (
            <small>Unavailable in this provider feed</small>
          ) : null}
        </label>
      </div>
    </section>
  );
}

function KpiCard({
  label,
  value,
  detail,
  unavailable = false,
}: {
  label: string;
  value: string;
  detail: string;
  unavailable?: boolean;
}): ReactNode {
  return (
    <article className={`dash-kpi-card ${unavailable ? "unavailable" : ""}`}>
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </article>
  );
}

function DailyChart({
  daily,
}: {
  daily: readonly AggregatedUsageDay[];
}): ReactNode {
  const bars = scaleChartBars(
    daily.map((day) => ({
      key: day.date,
      label: formatDateKey(day.date),
      value: day.totalTokens,
    })),
    { formatValue: (value) => `${exactNumber.format(value)} tokens` },
  );

  if (daily.length === 0) {
    return (
      <div className="dash-chart-empty">
        <strong>No activity in this range</strong>
        <span>Try a wider date range or clear a model or effort filter.</span>
      </div>
    );
  }

  const chartWidth = Math.max(640, daily.length * 22);
  const labelEvery = Math.max(1, Math.ceil(daily.length / 8));

  return (
    <div className="dash-chart-scroll">
      <div
        className="dash-chart"
        style={{ width: `${chartWidth}px` }}
        role="img"
        aria-label={`Daily token usage from ${formatDateKey(
          daily[0]?.date ?? "",
        )} through ${formatDateKey(daily.at(-1)?.date ?? "")}`}
      >
        <div className="dash-chart-grid" aria-hidden="true">
          <span />
          <span />
          <span />
          <span />
        </div>
        <div className="dash-chart-bars">
          {bars.map((bar) => (
            <span
              key={bar.key}
              className="dash-chart-bar"
              style={
                {
                  left: `${bar.xPercent}%`,
                  width: `${bar.widthPercent}%`,
                  height: `${bar.heightPercent ?? 0}%`,
                } as CSSProperties
              }
              title={bar.ariaLabel}
              aria-hidden="true"
            />
          ))}
        </div>
        <div className="dash-chart-labels" aria-hidden="true">
          {daily.map((day, index) =>
            index % labelEvery === 0 || index === daily.length - 1 ? (
              <span
                key={day.date}
                style={{ left: `${bars[index]?.xPercent ?? 0}%` }}
              >
                {formatDateKey(day.date).replace(/, \d{4}$/, "")}
              </span>
            ) : null,
          )}
        </div>
      </div>
    </div>
  );
}

function BreakdownChart({
  title,
  description,
  items,
  supported,
}: {
  title: string;
  description: string;
  items: readonly BreakdownItem[];
  supported: boolean;
}): ReactNode {
  const maximum = Math.max(0, ...items.map((item) => item.tokens));

  return (
    <article className="dash-panel dash-breakdown-panel">
      <div className="dash-panel-heading">
        <div>
          <h3>{title}</h3>
          <p>{description}</p>
        </div>
      </div>
      {!supported ? (
        <div className="dash-capability-note">
          <span aria-hidden="true">—</span>
          <div>
            <strong>Not available from this feed</strong>
            <p>UsageApp will not infer this dimension from aggregate totals.</p>
          </div>
        </div>
      ) : items.length === 0 ? (
        <div className="dash-compact-empty">No matching activity yet.</div>
      ) : (
        <div className="dash-breakdown-list">
          {items.slice(0, 8).map((item) => (
            <div className="dash-breakdown-row" key={item.label}>
              <div>
                <span title={item.label}>{item.label}</span>
                <strong>{compactNumber.format(item.tokens)}</strong>
              </div>
              <div className="dash-breakdown-track">
                <span
                  style={{
                    width:
                      maximum > 0
                        ? `${Math.max(2, (item.tokens / maximum) * 100)}%`
                        : "0%",
                  }}
                />
              </div>
            </div>
          ))}
        </div>
      )}
    </article>
  );
}

function TokenCategoryCards({
  input,
  output,
  cacheRead,
  cacheWrite,
  supported,
}: {
  input: number | null;
  output: number | null;
  cacheRead: number | null;
  cacheWrite: number | null;
  supported: boolean;
}): ReactNode {
  const metrics = [
    { label: "Input", value: input, tone: "input" },
    { label: "Output", value: output, tone: "output" },
    { label: "Cache read", value: cacheRead, tone: "cache-read" },
    { label: "Cache write", value: cacheWrite, tone: "cache-write" },
  ] as const;

  return (
    <div className="dash-token-grid">
      {metrics.map((metric) => (
        <article
          className={`dash-token-card ${metric.tone} ${
            !supported ? "unavailable" : ""
          }`}
          key={metric.label}
        >
          <span>{metric.label}</span>
          <strong>{supported ? formatCount(metric.value) : "—"}</strong>
          <small>{supported ? "tokens" : "Not supplied"}</small>
        </article>
      ))}
    </div>
  );
}

function DailyUsageTable({
  daily,
  provider,
}: {
  daily: readonly AggregatedUsageDay[];
  provider: ProviderDesktopState;
}): ReactNode {
  const capabilities = provider.analytics.capabilities;

  return (
    <div className="dash-table-wrap">
      <table className="dash-table">
        <caption className="dash-sr-only">
          Daily token usage for the selected provider, date range, model, and
          effort filters.
        </caption>
        <thead>
          <tr>
            <th scope="col">Date</th>
            <th scope="col">Total</th>
            {capabilities.tokenCategories ? (
              <>
                <th scope="col">Input</th>
                <th scope="col">Output</th>
                <th scope="col">Cache read</th>
                <th scope="col">Cache write</th>
              </>
            ) : null}
            {capabilities.estimatedCost ? (
              <th scope="col">Est. cost</th>
            ) : null}
            {daily.some((day) => day.requestCount !== null) ? (
              <th scope="col">Requests</th>
            ) : null}
          </tr>
        </thead>
        <tbody>
          {[...daily].reverse().map((day) => (
            <tr key={day.date}>
              <th scope="row">{formatDateKey(day.date)}</th>
              <td>{formatCount(day.totalTokens, true)}</td>
              {capabilities.tokenCategories ? (
                <>
                  <td>{formatCount(day.inputTokens, true)}</td>
                  <td>{formatCount(day.outputTokens, true)}</td>
                  <td>{formatCount(day.cacheReadTokens, true)}</td>
                  <td>{formatCount(day.cacheWriteTokens, true)}</td>
                </>
              ) : null}
              {capabilities.estimatedCost ? (
                <td>{formatMoney(day.estimatedCostUsd)}</td>
              ) : null}
              {daily.some((item) => item.requestCount !== null) ? (
                <td>{formatCount(day.requestCount, true)}</td>
              ) : null}
            </tr>
          ))}
          {daily.length === 0 ? (
            <tr>
              <td
                className="dash-table-empty"
                colSpan={
                  2 +
                  (capabilities.tokenCategories ? 4 : 0) +
                  (capabilities.estimatedCost ? 1 : 0) +
                  (daily.some((day) => day.requestCount !== null) ? 1 : 0)
                }
              >
                No activity matches the current filters.
              </td>
            </tr>
          ) : null}
        </tbody>
      </table>
    </div>
  );
}

export function DashboardView({
  state,
  onProviderChange,
  onRefresh,
  onConnectClaude,
  onDisconnectClaude,
  onUpdateSettings,
  actionError = null,
}: DashboardViewProps): ReactNode {
  const provider =
    state.providers.find((item) => item.id === state.activeProviderId) ??
    state.providers[0];
  const [preset, setPreset] = useState<DateRangePreset>("30d");
  const [customStart, setCustomStart] = useState("");
  const [customEnd, setCustomEnd] = useState("");
  const [selectedModel, setSelectedModel] = useState("all");
  const [selectedReasoning, setSelectedReasoning] = useState("all");

  const filterOptions = useMemo(
    () => getUsageFilterOptions(provider?.analytics.buckets ?? []),
    [provider?.analytics.buckets],
  );

  useEffect(() => {
    setSelectedModel("all");
    setSelectedReasoning("all");
  }, [provider?.id]);

  useEffect(() => {
    if (
      selectedModel !== "all" &&
      !filterOptions.models.includes(selectedModel)
    ) {
      setSelectedModel("all");
    }
    if (
      selectedReasoning !== "all" &&
      !filterOptions.reasoningLevels.includes(selectedReasoning)
    ) {
      setSelectedReasoning("all");
    }
  }, [
    filterOptions.models,
    filterOptions.reasoningLevels,
    selectedModel,
    selectedReasoning,
  ]);

  const range = useMemo(
    () =>
      resolveDateRange(preset, new Date(), {
        startDate: customStart || null,
        endDate: customEnd || null,
      }),
    [customEnd, customStart, preset],
  );

  const selected = useMemo(
    () =>
      selectAndAggregateUsage(provider?.analytics.buckets ?? [], {
        range,
        models: selectedModel === "all" ? null : [selectedModel],
        reasoningLevels:
          selectedReasoning === "all" ? null : [selectedReasoning],
      }),
    [provider?.analytics.buckets, range, selectedModel, selectedReasoning],
  );

  if (!provider) {
    return (
      <main className="dashboard-shell">
        <div className="dash-fatal-empty">No usage providers are available.</div>
      </main>
    );
  }

  const isClaude = provider.id === "anthropic-claude";
  const capabilities = provider.analytics.capabilities;
  const modelBreakdown = aggregateDimension(
    selected.selectedBuckets,
    "model",
  );
  const reasoningBreakdown = aggregateDimension(
    selected.selectedBuckets,
    "reasoningLevel",
  );
  const averagePerDay =
    selected.summary.dayCount > 0
      ? selected.summary.totalTokens / selected.summary.dayCount
      : null;
  const observedMinutes = selected.selectedBuckets.reduce(
    (total, bucket) => total + (bucket.observedMinutes ?? 0),
    0,
  );
  const tokensPerMinute = capabilities.tokensPerMinute && observedMinutes > 0
    ? selected.summary.totalTokens / observedMinutes
    : null;
  const snapshotProfile = provider.snapshot?.tokenUsage;
  const recordingLabel = isClaude
    ? provider.analytics.recordingSince
      ? `Local Claude Code activity recorded by UsageApp since ${formatTimestamp(
          provider.analytics.recordingSince,
        )}.`
      : "Local Claude Code activity will be recorded by UsageApp after you connect it."
    : "Account-level token activity returned by the documented Codex app-server.";
  const earliestDate = provider.analytics.buckets
    .map((bucket) => bucket.date)
    .sort()[0];
  const latestDate = provider.analytics.buckets
    .map((bucket) => bucket.date)
    .sort()
    .at(-1);

  const selectPreset = (nextPreset: DateRangePreset): void => {
    if (nextPreset === "custom") {
      setCustomStart((current) => current || earliestDate || localDateKey());
      setCustomEnd((current) => current || latestDate || localDateKey());
    }
    setPreset(nextPreset);
  };

  return (
    <main
      className="dashboard-shell"
      data-provider={providerTheme(provider)}
      style={{
        zoom: state.settings.dashboardTextScale / 100,
        fontFamily: interfaceFontFamily(state.settings.interfaceFont),
      }}
    >
      <header className="dash-header">
        <div className="dash-brand">
          <span className="dash-brand-mark">U</span>
          <div>
            <strong>UsageApp</strong>
            <span>Usage dashboard</span>
          </div>
        </div>

        <ProviderSelector
          providers={state.providers}
          activeProviderId={provider.id}
          awaitingClaude={state.claudeIntegration.state === "awaiting-session"}
          onChange={onProviderChange}
        />

        {onUpdateSettings ? (
          <label className="dash-text-size" title="Dashboard text size">
            <span>Text</span>
            <select value={state.settings.dashboardTextScale} onChange={(event) => void onUpdateSettings({ dashboardTextScale: Number(event.currentTarget.value) as 100 | 125 | 150 | 175 })}>
              <option value={100}>100%</option>
              <option value={125}>125%</option>
              <option value={150}>150%</option>
              <option value={175}>175%</option>
            </select>
          </label>
        ) : null}
        <button
          className="dash-refresh-button"
          type="button"
          disabled={
            provider.refreshPhase === "refreshing" ||
            provider.refreshPhase === "starting"
          }
          onClick={() => void onRefresh()}
        >
          <span
            className={
              provider.refreshPhase === "refreshing" ? "spinning" : ""
            }
            aria-hidden="true"
          >
            ↻
          </span>
          {provider.refreshPhase === "refreshing" ? "Refreshing" : "Refresh"}
        </button>
      </header>

      <div className="dash-content">
        {actionError ? (
          <div className="dash-error-banner" role="alert">
            {actionError}
          </div>
        ) : null}

        <section className="dash-intro">
          <div>
            <span className="dash-eyebrow">
              {isClaude ? "Anthropic Claude" : "OpenAI Codex"}
            </span>
            <h1>{provider.name} usage</h1>
            <p>
              Live plan limits stay separate from historical activity, so a
              token chart is never presented as quota remaining.
            </p>
          </div>
          <div className="dash-source-note">
            <span className="dash-source-icon" aria-hidden="true">
              i
            </span>
            <div>
              <strong>{recordingLabel}</strong>
              <span>
                {isClaude
                  ? "Shared Claude plan quota appears above; detailed history covers only local Claude Code activity captured after connection."
                  : "Codex account history currently supplies daily totals, not historical model or reasoning-level attribution."}
              </span>
            </div>
          </div>
        </section>

        {isClaude && !state.settings.claudeEnabled ? (
          <section className="dash-connect-card">
            <div>
              <span className="dash-eyebrow">Optional local integration</span>
              <h2>Connect Claude Code monitoring</h2>
              <p>
                UsageApp can receive documented status-line and telemetry
                events. Detailed history begins after connection; prompts,
                responses, credentials, and account IDs are not stored.
              </p>
            </div>
            {onConnectClaude ? (
              <button type="button" onClick={() => void onConnectClaude()}>
                Connect Claude
              </button>
            ) : null}
          </section>
        ) : isClaude && state.settings.claudeEnabled && onDisconnectClaude ? (
          <div className="dash-connection-row">
            <span>
              Claude monitoring: {state.claudeIntegration.message ?? "On"}
            </span>
            {state.claudeIntegration.state === "awaiting-session" ? <small>No separate prompt is expected. Restart Claude Code, then run a prompt.</small> : null}
            <button type="button" onClick={() => void onDisconnectClaude()}>
              Disconnect
            </button>
          </div>
        ) : null}

        <QuotaSection provider={provider} />

        <section
          className="dash-section dash-history-section"
          aria-labelledby="history-heading"
        >
          <div className="dash-section-heading">
            <div>
              <span className="dash-eyebrow">Activity history</span>
              <h2 id="history-heading">Explore usage</h2>
            </div>
            <span className="dash-observed-at">
              Activity updated {formatTimestamp(provider.analytics.observedAt)}
            </span>
          </div>

          <DateAndDimensionFilters
            provider={provider}
            preset={preset}
            customStart={customStart}
            customEnd={customEnd}
            selectedModel={selectedModel}
            selectedReasoning={selectedReasoning}
            onPresetChange={selectPreset}
            onCustomStartChange={setCustomStart}
            onCustomEndChange={setCustomEnd}
            onModelChange={setSelectedModel}
            onReasoningChange={setSelectedReasoning}
          />

          {provider.analytics.message ? (
            <div className="dash-feed-message">
              {provider.analytics.message}
            </div>
          ) : null}

          <div className="dash-kpi-grid">
            <KpiCard
              label="Selected tokens"
              value={formatCount(selected.summary.totalTokens)}
              detail={`${selected.summary.dayCount} active ${
                selected.summary.dayCount === 1 ? "day" : "days"
              }`}
            />
            <KpiCard
              label="Average per active day"
              value={formatCount(averagePerDay)}
              detail="Across days with recorded activity"
            />
            <KpiCard
              label="Requests"
              value={formatCount(selected.summary.requestCount)}
              detail={
                selected.summary.requestCount === null
                  ? "Not supplied by this feed"
                  : "Selected API requests"
              }
              unavailable={selected.summary.requestCount === null}
            />
            <KpiCard
              label="Observed tokens/min"
              value={formatCount(tokensPerMinute)}
              detail={capabilities.tokensPerMinute ? "Across captured request spans; filterable by model and effort" : "Not supplied by this provider feed"}
              unavailable={!capabilities.tokensPerMinute || tokensPerMinute === null}
            />
            <KpiCard
              label="Estimated cost"
              value={formatMoney(selected.summary.estimatedCostUsd)}
              detail={
                capabilities.estimatedCost
                  ? "Provider telemetry estimate"
                  : "Not supplied by this feed"
              }
              unavailable={!capabilities.estimatedCost}
            />
          </div>

          <div className="dash-primary-grid">
            <article className="dash-panel dash-chart-panel">
              <div className="dash-panel-heading">
                <div>
                  <h3>Tokens by day</h3>
                  <p>
                    Daily totals for the selected range
                    {selectedModel !== "all" ? ` · ${selectedModel}` : ""}
                    {selectedReasoning !== "all"
                      ? ` · ${selectedReasoning}`
                      : ""}
                  </p>
                </div>
                <strong>{formatCount(selected.summary.totalTokens)}</strong>
              </div>
              <DailyChart daily={selected.daily} />
            </article>

            <aside className="dash-panel dash-profile-panel">
              <div className="dash-panel-heading">
                <div>
                  <h3>{isClaude ? "Live session" : "Profile highlights"}</h3>
                  <p>
                    {isClaude
                      ? "Latest sanitized Claude status-line details"
                      : "Account summary, independent of the filters"}
                  </p>
                </div>
              </div>
              {isClaude ? (
                <dl>
                  <div>
                    <dt>Model</dt>
                    <dd title={provider.liveDetails?.model ?? undefined}>
                      {provider.liveDetails?.model ?? "—"}
                    </dd>
                  </div>
                  <div>
                    <dt>Effort</dt>
                    <dd>{provider.liveDetails?.reasoningLevel ?? "—"}</dd>
                  </div>
                  <div>
                    <dt>Current input</dt>
                    <dd>
                      {formatCount(provider.liveDetails?.inputTokens ?? null)}
                    </dd>
                  </div>
                  <div>
                    <dt>Current output</dt>
                    <dd>
                      {formatCount(provider.liveDetails?.outputTokens ?? null)}
                    </dd>
                  </div>
                  <div>
                    <dt>Thinking</dt>
                    <dd>
                      {provider.liveDetails?.thinkingEnabled === null ||
                      provider.liveDetails?.thinkingEnabled === undefined
                        ? "—"
                        : provider.liveDetails.thinkingEnabled
                          ? "On"
                          : "Off"}
                    </dd>
                  </div>
                  <div>
                    <dt>Session cost</dt>
                    <dd>
                      {formatMoney(
                        provider.liveDetails?.estimatedSessionCostUsd ?? null,
                      )}
                    </dd>
                  </div>
                </dl>
              ) : (
                <dl>
                  <div>
                    <dt>Lifetime tokens</dt>
                    <dd>
                      {formatCount(snapshotProfile?.lifetimeTokens ?? null)}
                    </dd>
                  </div>
                  <div>
                    <dt>Peak day</dt>
                    <dd>
                      {formatCount(snapshotProfile?.peakDailyTokens ?? null)}
                    </dd>
                  </div>
                  <div>
                    <dt>Current streak</dt>
                    <dd>
                      {snapshotProfile?.currentStreakDays === null ||
                      snapshotProfile?.currentStreakDays === undefined
                        ? "—"
                        : `${snapshotProfile.currentStreakDays}d`}
                    </dd>
                  </div>
                  <div>
                    <dt>Longest task</dt>
                    <dd>
                      {snapshotProfile?.longestRunningTurnSec === null ||
                      snapshotProfile?.longestRunningTurnSec === undefined
                        ? "—"
                        : `${Math.round(
                            snapshotProfile.longestRunningTurnSec / 60,
                          )}m`}
                    </dd>
                  </div>
                </dl>
              )}
              {isClaude && !provider.liveDetails ? (
                <p className="dash-profile-note">
                  Start a new Claude Code session after connecting to populate
                  this live card.
                </p>
              ) : !isClaude && !snapshotProfile ? (
                <p className="dash-profile-note">
                  This provider does not supply an account profile summary.
                </p>
              ) : null}
            </aside>
          </div>

          <div className="dash-section-heading dash-subheading">
            <div>
              <span className="dash-eyebrow">Token composition</span>
              <h2>Token categories</h2>
            </div>
            {!capabilities.tokenCategories ? (
              <span className="dash-unavailable-tag">Not supplied</span>
            ) : null}
          </div>

          <TokenCategoryCards
            input={selected.summary.inputTokens}
            output={selected.summary.outputTokens}
            cacheRead={selected.summary.cacheReadTokens}
            cacheWrite={selected.summary.cacheWriteTokens}
            supported={capabilities.tokenCategories}
          />

          <div className="dash-breakdown-grid">
            <BreakdownChart
              title="By model"
              description="Selected token total grouped by model"
              items={modelBreakdown}
              supported={capabilities.modelFilter}
            />
            <BreakdownChart
              title={isClaude ? "By effort" : "By reasoning level"}
              description={
                isClaude
                  ? "Normal token usage grouped by the reported effort setting"
                  : "Selected token total grouped by reasoning level"
              }
              items={reasoningBreakdown}
              supported={capabilities.reasoningFilter}
            />
          </div>

          <article className="dash-panel dash-table-panel">
            <div className="dash-panel-heading">
              <div>
                <h3>Daily details</h3>
                <p>
                  Exact values for the chart above ·{" "}
                  {selected.daily.length}{" "}
                  {selected.daily.length === 1 ? "row" : "rows"}
                </p>
              </div>
            </div>
            <DailyUsageTable daily={selected.daily} provider={provider} />
          </article>
        </section>
      </div>
    </main>
  );
}
