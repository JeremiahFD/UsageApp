import { readFile, stat } from "node:fs/promises";
import { homedir } from "node:os";
import { join } from "node:path";

import type { UsageSnapshot, UsageWindow } from "@usageapp/core";

const CLAUDE_PROVIDER_ID = "anthropic-claude";
const CLAUDE_PROVIDER_NAME = "Claude";
const PLAN_USAGE_FILE_NAME = "plan-usage-history.json";
const DEFAULT_POLL_INTERVAL_MS = 60_000;

/**
 * The Claude desktop app refreshes this record on its own five-minute timer,
 * so a sample older than this means the desktop app is not running and the
 * fallback has nothing current to offer.
 */
export const CLAUDE_PLAN_USAGE_STALE_AFTER_MS = 15 * 60_000;

/**
 * One sanitized plan-usage observation. The on-disk record also carries an
 * organization identifier, which is deliberately dropped here: UsageApp only
 * needs the two utilization percentages.
 */
export interface ClaudePlanUsageSample {
  observedAt: string;
  fiveHourPercent: number | null;
  sevenDayPercent: number | null;
}

export interface ClaudePlanUsageWatcherOptions {
  /** Test/portable override for %APPDATA%\Claude\plan-usage-history.json. */
  filePath?: string;
  pollIntervalMs?: number;
  onSample: (sample: ClaudePlanUsageSample | null) => void;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function utilizationPercent(value: unknown): number | null {
  return typeof value === "number" &&
    Number.isFinite(value) &&
    value >= 0 &&
    value <= 100
    ? value
    : null;
}

export function claudePlanUsagePath(
  environment: NodeJS.ProcessEnv = process.env,
): string {
  const appData =
    typeof environment.APPDATA === "string" &&
    environment.APPDATA.trim().length > 0
      ? environment.APPDATA
      : join(homedir(), "AppData", "Roaming");
  return join(appData, "Claude", PLAN_USAGE_FILE_NAME);
}

/**
 * Reduces the desktop app's sample history to its most recent usable entry.
 *
 * The file layout is an undocumented implementation detail of another
 * application, so every field is treated as untrusted and a shape this does
 * not recognize yields null rather than a partial reading.
 */
export function parseClaudePlanUsage(
  rawValue: unknown,
  now: Date = new Date(),
): ClaudePlanUsageSample | null {
  if (!isRecord(rawValue) || !Array.isArray(rawValue.samples)) {
    return null;
  }

  // Tolerate clock skew, but never accept a sample claiming to be from the
  // future far enough that it would outrank a genuinely current reading.
  const latestAcceptableMs = now.getTime() + 60_000;
  let best: ClaudePlanUsageSample | null = null;
  let bestMs = Number.NEGATIVE_INFINITY;

  for (const entry of rawValue.samples) {
    if (!isRecord(entry)) continue;
    const timestampMs = entry.t;
    if (
      typeof timestampMs !== "number" ||
      !Number.isFinite(timestampMs) ||
      timestampMs <= 0 ||
      timestampMs > latestAcceptableMs ||
      timestampMs <= bestMs
    ) {
      continue;
    }

    const usage = isRecord(entry.u) ? entry.u : null;
    if (!usage) continue;
    const fiveHourPercent = utilizationPercent(usage.fh);
    const sevenDayPercent = utilizationPercent(usage.sd);
    if (fiveHourPercent === null && sevenDayPercent === null) continue;

    const observedAt = new Date(timestampMs);
    if (Number.isNaN(observedAt.getTime())) continue;

    bestMs = timestampMs;
    best = {
      observedAt: observedAt.toISOString(),
      fiveHourPercent,
      sevenDayPercent,
    };
  }

  return best;
}

function planUsageWindow(
  usedPercent: number | null,
  descriptor: {
    id: string;
    kind: "primary" | "secondary";
    label: string;
    durationMinutes: number;
  },
): UsageWindow | null {
  if (usedPercent === null) return null;
  return {
    id: descriptor.id,
    limitId: "claude",
    limitName: null,
    kind: descriptor.kind,
    label: descriptor.label,
    usedPercent,
    remainingPercent: 100 - usedPercent,
    durationMinutes: descriptor.durationMinutes,
    // This source reports utilization only. Reset times come from the status
    // line when a Claude Code session has supplied them.
    resetsAt: null,
  };
}

export function snapshotFromClaudePlanUsage(
  sample: ClaudePlanUsageSample,
  now: Date = new Date(),
  staleAfterMs: number = CLAUDE_PLAN_USAGE_STALE_AFTER_MS,
): UsageSnapshot {
  const windows: UsageWindow[] = [];
  const fiveHour = planUsageWindow(sample.fiveHourPercent, {
    id: "claude:five-hour",
    kind: "primary",
    label: "5-hour",
    durationMinutes: 300,
  });
  if (fiveHour) windows.push(fiveHour);

  const sevenDay = planUsageWindow(sample.sevenDayPercent, {
    id: "claude:seven-day",
    kind: "secondary",
    label: "Weekly",
    durationMinutes: 10_080,
  });
  if (sevenDay) windows.push(sevenDay);

  const observedAtMs = Date.parse(sample.observedAt);
  const tooOld =
    !Number.isFinite(observedAtMs) ||
    now.getTime() - observedAtMs > staleAfterMs;

  return {
    schemaVersion: 1,
    providerId: CLAUDE_PROVIDER_ID,
    providerName: CLAUDE_PROVIDER_NAME,
    observedAt: sample.observedAt,
    status: windows.length === 0 ? "unavailable" : tooOld ? "stale" : "live",
    windows,
    bankedResets: {
      availableCount: null,
      detailsAvailable: false,
      items: [],
    },
    credits: null,
    planType: null,
    tokenUsage: null,
    message:
      windows.length === 0
        ? "The Claude desktop app has not recorded plan usage yet."
        : tooOld
          ? "Showing the last plan usage the Claude desktop app recorded."
          : null,
  };
}

/**
 * Polls the Claude desktop app's plan-usage record.
 *
 * This is the only Claude quota source that updates without a Claude Code
 * session, so it is what keeps the tray current while nothing is running. It
 * is a best-effort supplement: a missing file, an unreadable file, or an
 * unrecognized layout simply reports no sample.
 */
export class ClaudePlanUsageWatcher {
  private readonly filePath: string;
  private readonly pollIntervalMs: number;
  private readonly onSample: (
    sample: ClaudePlanUsageSample | null,
  ) => void;

  private timer: NodeJS.Timeout | null = null;
  private lastModifiedMs: number | null = null;
  private latestSample: ClaudePlanUsageSample | null = null;
  private reading = false;

  constructor(options: ClaudePlanUsageWatcherOptions) {
    this.filePath = options.filePath ?? claudePlanUsagePath();
    this.pollIntervalMs = options.pollIntervalMs ?? DEFAULT_POLL_INTERVAL_MS;
    this.onSample = options.onSample;
  }

  get sample(): ClaudePlanUsageSample | null {
    return this.latestSample;
  }

  start(): void {
    if (this.timer) return;
    this.timer = setInterval(() => {
      void this.read();
    }, this.pollIntervalMs);
    this.timer.unref();
    void this.read();
  }

  stop(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
    // Drop the read cache so a later start reports its first sample again
    // rather than suppressing it as unchanged.
    this.lastModifiedMs = null;
    this.latestSample = null;
  }

  /** Re-reads only when the file changed, so polling stays cheap. */
  async read(force = false): Promise<ClaudePlanUsageSample | null> {
    if (this.reading) return this.latestSample;
    this.reading = true;
    try {
      let modifiedMs: number;
      try {
        modifiedMs = (await stat(this.filePath)).mtimeMs;
      } catch {
        // No desktop app, no permission, or the file was removed.
        if (this.latestSample !== null) {
          this.latestSample = null;
          this.lastModifiedMs = null;
          this.onSample(null);
        }
        return null;
      }

      if (!force && modifiedMs === this.lastModifiedMs) {
        return this.latestSample;
      }
      this.lastModifiedMs = modifiedMs;

      let parsed: unknown;
      try {
        parsed = JSON.parse(await readFile(this.filePath, "utf8")) as unknown;
      } catch {
        // A torn read during the desktop app's own write is expected; the next
        // poll picks the file up again.
        return this.latestSample;
      }

      const sample = parseClaudePlanUsage(parsed);
      if (sample === null) {
        return this.latestSample;
      }
      if (sample.observedAt !== this.latestSample?.observedAt) {
        this.latestSample = sample;
        this.onSample(sample);
      }
      return this.latestSample;
    } finally {
      this.reading = false;
    }
  }
}
