import type {
  BankedReset,
  CreditSummary,
  TokenUsageDailyBucket,
  TokenUsageSummary,
  UsageSnapshot,
  UsageWindow,
} from "./types";

type JsonObject = Record<string, unknown>;

interface RawRateLimitWindow {
  usedPercent?: unknown;
  windowDurationMins?: unknown;
  resetsAt?: unknown;
}

interface RawRateLimitSnapshot {
  limitId?: unknown;
  limitName?: unknown;
  primary?: unknown;
  secondary?: unknown;
  credits?: unknown;
  planType?: unknown;
}

function isObject(value: unknown): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function finiteNumber(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "bigint") {
    const converted = Number(value);
    return Number.isSafeInteger(converted) ? converted : null;
  }
  return null;
}

function stringOrNull(value: unknown): string | null {
  return typeof value === "string" ? value : null;
}

function epochSecondsToIso(value: unknown): string | null {
  const seconds = finiteNumber(value);
  if (seconds === null || seconds <= 0) {
    return null;
  }
  const date = new Date(seconds * 1_000);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function clampPercent(value: number): number {
  return Math.min(100, Math.max(0, value));
}

export function formatWindowDuration(durationMinutes: number | null): string {
  if (durationMinutes === null || durationMinutes <= 0) {
    return "Usage window";
  }
  if (durationMinutes === 10_080) {
    return "Weekly";
  }
  if (durationMinutes % 1_440 === 0) {
    const days = durationMinutes / 1_440;
    return `${days}-day`;
  }
  if (durationMinutes % 60 === 0) {
    const hours = durationMinutes / 60;
    return `${hours}-hour`;
  }
  return `${durationMinutes}-minute`;
}

function makeWindowLabel(
  limitId: string,
  limitName: string | null,
  durationMinutes: number | null,
): string {
  const duration = formatWindowDuration(durationMinutes);
  if (limitName && limitId !== "codex") {
    return `${limitName} · ${duration}`;
  }
  return duration;
}

function normalizeWindow(
  raw: unknown,
  snapshot: RawRateLimitSnapshot,
  kind: "primary" | "secondary",
  fallbackLimitId: string,
): UsageWindow | null {
  if (!isObject(raw)) {
    return null;
  }
  const window = raw as RawRateLimitWindow;
  const used = finiteNumber(window.usedPercent);
  if (used === null) {
    return null;
  }

  const limitId = stringOrNull(snapshot.limitId) ?? fallbackLimitId;
  const limitName = stringOrNull(snapshot.limitName);
  const rawDurationMinutes = finiteNumber(window.windowDurationMins);
  const durationMinutes =
    rawDurationMinutes !== null && rawDurationMinutes >= 0
      ? Math.trunc(rawDurationMinutes)
      : null;
  const usedPercent = clampPercent(used);

  return {
    id: `${limitId}:${kind}`,
    limitId,
    limitName,
    kind,
    label: makeWindowLabel(limitId, limitName, durationMinutes),
    usedPercent,
    remainingPercent: clampPercent(100 - usedPercent),
    durationMinutes,
    resetsAt: epochSecondsToIso(window.resetsAt),
  };
}

function rateLimitSnapshots(response: JsonObject): Array<[string, RawRateLimitSnapshot]> {
  const byId = response.rateLimitsByLimitId;
  if (isObject(byId)) {
    const entries = Object.entries(byId).filter(
      (entry): entry is [string, RawRateLimitSnapshot] => isObject(entry[1]),
    );
    if (entries.length > 0) {
      return entries;
    }
  }

  if (isObject(response.rateLimits)) {
    const raw = response.rateLimits as RawRateLimitSnapshot;
    return [[stringOrNull(raw.limitId) ?? "codex", raw]];
  }
  return [];
}

function normalizeCredits(raw: unknown): CreditSummary | null {
  if (
    !isObject(raw) ||
    typeof raw.hasCredits !== "boolean" ||
    typeof raw.unlimited !== "boolean"
  ) {
    return null;
  }
  return {
    hasCredits: raw.hasCredits,
    unlimited: raw.unlimited,
    balance: stringOrNull(raw.balance),
  };
}

function normalizeBankedResets(response: JsonObject): UsageSnapshot["bankedResets"] {
  const summary = response.rateLimitResetCredits;
  if (!isObject(summary)) {
    return { availableCount: null, detailsAvailable: false, items: [] };
  }

  const rawCount = finiteNumber(summary.availableCount);
  const count =
    rawCount !== null && rawCount >= 0 ? Math.trunc(rawCount) : null;
  const credits = summary.credits;
  const detailsAvailable = Array.isArray(credits);
  const items: BankedReset[] = [];

  if (Array.isArray(credits)) {
    for (const credit of credits) {
      if (!isObject(credit) || typeof credit.id !== "string") {
        continue;
      }
      const grantedAt = epochSecondsToIso(credit.grantedAt);
      if (!grantedAt) {
        continue;
      }
      items.push({
        id: credit.id,
        title: stringOrNull(credit.title),
        description: stringOrNull(credit.description),
        status: stringOrNull(credit.status) ?? "unknown",
        grantedAt,
        expiresAt: epochSecondsToIso(credit.expiresAt),
      });
    }
  }

  items.sort((left, right) => {
    if (!left.expiresAt && !right.expiresAt) return 0;
    if (!left.expiresAt) return 1;
    if (!right.expiresAt) return -1;
    return left.expiresAt.localeCompare(right.expiresAt);
  });

  return {
    availableCount: count,
    detailsAvailable,
    items,
  };
}

function nullableUsageInteger(value: unknown): number | null {
  if (value === null) {
    return null;
  }
  const number = finiteNumber(value);
  return number !== null && number >= 0 ? Math.trunc(number) : null;
}

function isIsoDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return false;
  }
  const parsed = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().startsWith(value);
}

function normalizeTokenUsage(raw: unknown): TokenUsageSummary | null {
  if (!isObject(raw) || !isObject(raw.summary)) {
    return null;
  }

  let dailyUsageBuckets: TokenUsageDailyBucket[] | null = null;
  if (Array.isArray(raw.dailyUsageBuckets)) {
    dailyUsageBuckets = raw.dailyUsageBuckets.flatMap((bucket) => {
      if (!isObject(bucket) || typeof bucket.startDate !== "string") {
        return [];
      }
      const tokens = nullableUsageInteger(bucket.tokens);
      return tokens === null || !isIsoDate(bucket.startDate)
        ? []
        : [{ startDate: bucket.startDate, tokens }];
    });
  }

  return {
    lifetimeTokens: nullableUsageInteger(raw.summary.lifetimeTokens),
    peakDailyTokens: nullableUsageInteger(raw.summary.peakDailyTokens),
    longestRunningTurnSec: nullableUsageInteger(
      raw.summary.longestRunningTurnSec,
    ),
    currentStreakDays: nullableUsageInteger(raw.summary.currentStreakDays),
    longestStreakDays: nullableUsageInteger(raw.summary.longestStreakDays),
    dailyUsageBuckets,
  };
}

/**
 * Converts the stable Codex app-server account responses into the versioned,
 * provider-neutral snapshot sent to every UsageApp client.
 */
export function normalizeCodexSnapshot(
  rawRateLimitsResponse: unknown,
  rawUsageResponse: unknown,
  now = new Date(),
): UsageSnapshot {
  const response = isObject(rawRateLimitsResponse) ? rawRateLimitsResponse : {};
  const snapshots = rateLimitSnapshots(response);
  const windows: UsageWindow[] = [];

  for (const [fallbackId, snapshot] of snapshots) {
    const primary = normalizeWindow(snapshot.primary, snapshot, "primary", fallbackId);
    const secondary = normalizeWindow(
      snapshot.secondary,
      snapshot,
      "secondary",
      fallbackId,
    );
    if (primary) windows.push(primary);
    if (secondary) windows.push(secondary);
  }

  const preferred =
    snapshots.find(([id]) => id === "codex")?.[1] ?? snapshots.at(0)?.[1] ?? null;
  const credits =
    normalizeCredits(preferred?.credits) ??
    snapshots.map(([, value]) => normalizeCredits(value.credits)).find(Boolean) ??
    null;
  const planType =
    stringOrNull(preferred?.planType) ??
    snapshots.map(([, value]) => stringOrNull(value.planType)).find(Boolean) ??
    null;

  return {
    schemaVersion: 1,
    providerId: "openai-codex",
    providerName: "Codex",
    observedAt: now.toISOString(),
    status: "live",
    windows,
    bankedResets: normalizeBankedResets(response),
    credits,
    planType,
    tokenUsage: normalizeTokenUsage(rawUsageResponse),
    message: windows.length === 0 ? "Codex did not return any usage windows." : null,
  };
}

export function createUnavailableSnapshot(
  status: "auth-required" | "unavailable",
  message: string,
  now = new Date(),
): UsageSnapshot {
  return {
    schemaVersion: 1,
    providerId: "openai-codex",
    providerName: "Codex",
    observedAt: now.toISOString(),
    status,
    windows: [],
    bankedResets: { availableCount: null, detailsAvailable: false, items: [] },
    credits: null,
    planType: null,
    tokenUsage: null,
    message,
  };
}

export function markSnapshotStale(
  snapshot: UsageSnapshot,
  message = "Showing the last successful update.",
): UsageSnapshot {
  return { ...snapshot, status: "stale", message };
}
