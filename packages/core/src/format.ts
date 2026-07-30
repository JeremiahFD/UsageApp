import type { CreditSummary, TraySummary, UsageSnapshot } from "./types";

const compactNumber = new Intl.NumberFormat(undefined, {
  notation: "compact",
  maximumFractionDigits: 1,
});

export function formatPercent(value: number): string {
  return `${Math.round(value)}%`;
}

export function formatTokenCount(value: number | null): string {
  return value === null ? "Unavailable" : compactNumber.format(value);
}

export interface CreditDisplay {
  /** The one value worth reading at a glance. */
  headline: string;
  /** Short qualifier, or null when the headline says everything. */
  detail: string | null;
  /** Drives emphasis: a zero balance should stay quiet, a block should not. */
  tone: "none" | "normal" | "warning";
  /** False when the provider reports no credit facility at all. */
  present: boolean;
}

/**
 * Describes a credit balance for display.
 *
 * Deliberately conservative: the provider supplies only a formatted balance,
 * an availability flag, and a spend-control flag, so this reports exactly
 * those. There is no credit limit, renewal date, or payment-method field in
 * the payload, and none is inferred here.
 */
export function describeCredits(
  credits: CreditSummary | null,
): CreditDisplay {
  if (credits === null) {
    return {
      headline: "None",
      detail: null,
      tone: "none",
      present: false,
    };
  }
  if (credits.spendControlReached === true) {
    return {
      headline: credits.unlimited ? "Unlimited" : credits.balance ?? "—",
      detail: "Spending limit reached",
      tone: "warning",
      present: true,
    };
  }
  if (credits.unlimited) {
    return {
      headline: "Unlimited",
      detail: null,
      tone: "normal",
      present: true,
    };
  }
  if (!credits.hasCredits) {
    // A zero balance is the ordinary state on a subscription plan, so it is
    // reported plainly rather than as a problem.
    return {
      headline: "None",
      detail: credits.balance === null ? null : `Balance ${credits.balance}`,
      tone: "none",
      present: true,
    };
  }
  return {
    headline: credits.balance ?? "Available",
    detail: credits.balance === null ? null : "Available",
    tone: "normal",
    present: true,
  };
}

export function formatResetTime(
  iso: string | null,
  now = new Date(),
): string {
  if (!iso) {
    return "Reset time unavailable";
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return "Reset time unavailable";
  }

  const sameDay = date.toDateString() === now.toDateString();
  const tomorrow = new Date(now);
  tomorrow.setDate(now.getDate() + 1);
  const isTomorrow = date.toDateString() === tomorrow.toDateString();
  const time = new Intl.DateTimeFormat(undefined, {
    hour: "numeric",
    minute: "2-digit",
  }).format(date);

  if (sameDay) return `Today at ${time}`;
  if (isTomorrow) return `Tomorrow at ${time}`;
  return new Intl.DateTimeFormat(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(date);
}

export function formatObservedTime(iso: string | null): string {
  if (!iso) return "Date unavailable";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "Date unavailable";
  return new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(date);
}

export function formatRelativeTime(
  iso: string | null,
  now = new Date(),
): string {
  if (!iso) {
    return "time unavailable";
  }
  const target = new Date(iso);
  if (Number.isNaN(target.getTime())) {
    return "time unavailable";
  }

  const deltaMs = target.getTime() - now.getTime();
  const future = deltaMs >= 0;
  const totalMinutes = Math.max(0, Math.round(Math.abs(deltaMs) / 60_000));
  const days = Math.floor(totalMinutes / 1_440);
  const hours = Math.floor((totalMinutes % 1_440) / 60);
  const minutes = totalMinutes % 60;

  let value: string;
  if (days > 0) {
    value = hours > 0 ? `${days}d ${hours}h` : `${days}d`;
  } else if (hours > 0) {
    value = minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`;
  } else {
    value = `${minutes}m`;
  }
  return future ? `in ${value}` : `${value} ago`;
}

export function getMostConstrainedRemaining(
  snapshot: UsageSnapshot,
): number | null {
  if (snapshot.windows.length === 0) {
    return null;
  }
  return Math.min(...snapshot.windows.map((window) => window.remainingPercent));
}

function shortWindowLabel(label: string): string {
  return label
    .replace("5-hour", "5h")
    .replace("Weekly", "Week")
    .replace("-hour", "h")
    .replace("-day", "d");
}

export function summarizeForTray(snapshot: UsageSnapshot): TraySummary {
  const percentage = getMostConstrainedRemaining(snapshot);
  const observedLabel = `Last known usage ${formatObservedTime(snapshot.observedAt)} (${formatRelativeTime(snapshot.observedAt)})`;
  if (snapshot.status === "auth-required") {
    return {
      percentage: null,
      tooltip: `${snapshot.providerName} Usage — sign in required • ${observedLabel}`,
      nextResetAt: null,
    };
  }
  if (snapshot.windows.length === 0) {
    return {
      percentage: null,
      tooltip: `${snapshot.providerName} Usage — ${
        snapshot.status === "live" ? "no limits returned" : "unavailable"
      } • ${observedLabel}`,
      nextResetAt: null,
    };
  }

  const windows = [...snapshot.windows].sort(
    (left, right) =>
      (left.durationMinutes ?? 0) - (right.durationMinutes ?? 0),
  );
  const fragments = windows
    .slice(0, 2)
    .map(
      (window) =>
        `${shortWindowLabel(window.label)} ${Math.round(window.remainingPercent)}% left`,
    );
  const resetTimes = windows
    .map((window) => window.resetsAt)
    .filter((value): value is string => Boolean(value))
    .map((value) => ({ value, timestamp: Date.parse(value) }))
    .filter(
      ({ timestamp }) =>
        Number.isFinite(timestamp) && timestamp >= Date.now(),
    )
    .sort((left, right) => left.timestamp - right.timestamp);

  const stalePrefix = snapshot.status === "stale" ? "Stale — " : "";

  return {
    percentage,
    tooltip: `${snapshot.providerName} Usage — ${stalePrefix}${fragments.join(" • ")} • ${observedLabel}`,
    nextResetAt: resetTimes.at(0)?.value ?? null,
  };
}
