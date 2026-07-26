import type {
  BankedReset,
  BankedResetSummary,
  CreditSummary,
  SnapshotStatus,
  TokenUsageDailyBucket,
  TokenUsageSummary,
  UsageSnapshot,
  UsageWindow,
} from "./types";

type JsonObject = Record<string, unknown>;

function isObject(value: unknown): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === "string";
}

function isNonnegativeInteger(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= 0;
}

function isPercentage(value: unknown): value is number {
  return (
    typeof value === "number" &&
    Number.isFinite(value) &&
    value >= 0 &&
    value <= 100
  );
}

function isDateTime(value: unknown): value is string {
  return typeof value === "string" && Number.isFinite(Date.parse(value));
}

function isNullableDateTime(value: unknown): value is string | null {
  return value === null || isDateTime(value);
}

function isUsageWindow(value: unknown): value is UsageWindow {
  if (!isObject(value)) return false;
  return (
    typeof value.id === "string" &&
    typeof value.limitId === "string" &&
    isNullableString(value.limitName) &&
    (value.kind === "primary" || value.kind === "secondary") &&
    typeof value.label === "string" &&
    isPercentage(value.usedPercent) &&
    isPercentage(value.remainingPercent) &&
    (value.durationMinutes === null ||
      isNonnegativeInteger(value.durationMinutes)) &&
    isNullableDateTime(value.resetsAt)
  );
}

function isBankedReset(value: unknown): value is BankedReset {
  if (!isObject(value)) return false;
  return (
    typeof value.id === "string" &&
    isNullableString(value.title) &&
    isNullableString(value.description) &&
    typeof value.status === "string" &&
    isDateTime(value.grantedAt) &&
    isNullableDateTime(value.expiresAt)
  );
}

function isBankedResetSummary(value: unknown): value is BankedResetSummary {
  if (!isObject(value)) return false;
  return (
    (value.availableCount === null ||
      isNonnegativeInteger(value.availableCount)) &&
    typeof value.detailsAvailable === "boolean" &&
    Array.isArray(value.items) &&
    value.items.every(isBankedReset)
  );
}

function isCreditSummary(value: unknown): value is CreditSummary {
  if (!isObject(value)) return false;
  return (
    typeof value.hasCredits === "boolean" &&
    typeof value.unlimited === "boolean" &&
    isNullableString(value.balance)
  );
}

function isNullableUsageInteger(value: unknown): boolean {
  return value === null || isNonnegativeInteger(value);
}

function isDailyBucket(value: unknown): value is TokenUsageDailyBucket {
  if (!isObject(value)) return false;
  if (
    typeof value.startDate !== "string" ||
    !/^\d{4}-\d{2}-\d{2}$/.test(value.startDate) ||
    !isNonnegativeInteger(value.tokens)
  ) {
    return false;
  }
  const parsed = new Date(`${value.startDate}T00:00:00.000Z`);
  return (
    !Number.isNaN(parsed.getTime()) &&
    parsed.toISOString().startsWith(value.startDate)
  );
}

function isTokenUsageSummary(value: unknown): value is TokenUsageSummary {
  if (!isObject(value)) return false;
  return (
    isNullableUsageInteger(value.lifetimeTokens) &&
    isNullableUsageInteger(value.peakDailyTokens) &&
    isNullableUsageInteger(value.longestRunningTurnSec) &&
    isNullableUsageInteger(value.currentStreakDays) &&
    isNullableUsageInteger(value.longestStreakDays) &&
    (value.dailyUsageBuckets === null ||
      (Array.isArray(value.dailyUsageBuckets) &&
        value.dailyUsageBuckets.every(isDailyBucket)))
  );
}

function isSnapshotStatus(value: unknown): value is SnapshotStatus {
  return (
    value === "live" ||
    value === "stale" ||
    value === "auth-required" ||
    value === "unavailable"
  );
}

/**
 * Exhaustively validates the read-only LAN/cache boundary before UI code sees
 * a snapshot. Invalid values are quarantined instead of being persisted.
 */
export function decodeUsageSnapshot(value: unknown): UsageSnapshot | null {
  if (!isObject(value)) return null;
  if (
    value.schemaVersion !== 1 ||
    typeof value.providerId !== "string" ||
    value.providerId.length === 0 ||
    typeof value.providerName !== "string" ||
    value.providerName.length === 0 ||
    !isDateTime(value.observedAt) ||
    !isSnapshotStatus(value.status) ||
    !Array.isArray(value.windows) ||
    !value.windows.every(isUsageWindow) ||
    !isBankedResetSummary(value.bankedResets) ||
    (value.credits !== null && !isCreditSummary(value.credits)) ||
    !isNullableString(value.planType) ||
    (value.tokenUsage !== null && !isTokenUsageSummary(value.tokenUsage)) ||
    !isNullableString(value.message)
  ) {
    return null;
  }
  return value as unknown as UsageSnapshot;
}
