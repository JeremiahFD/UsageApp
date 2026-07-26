import type { TraySummary, UsageSnapshot } from "./types";

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
  if (snapshot.status === "auth-required") {
    return {
      percentage: null,
      tooltip: "Codex Usage — sign in to Codex",
      nextResetAt: null,
    };
  }
  if (snapshot.windows.length === 0) {
    return {
      percentage: null,
      tooltip: `Codex Usage — ${snapshot.status === "live" ? "no limits returned" : "unavailable"}`,
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
    tooltip: `Codex Usage — ${stalePrefix}${fragments.join(" • ")}`,
    nextResetAt: resetTimes.at(0)?.value ?? null,
  };
}
