import { randomBytes } from "node:crypto";
import { mkdir, readFile, rename, unlink, writeFile } from "node:fs/promises";
import { dirname } from "node:path";

import type { UsageSnapshot, UsageWindow } from "@usageapp/core";
import type { ProviderLiveDetails } from "../shared/desktop";

const STORE_VERSION = 1;

interface StoredClaudeSnapshot {
  version: typeof STORE_VERSION;
  snapshot: UsageSnapshot;
  liveDetails: ProviderLiveDetails | null;
}

export interface ClaudeSnapshotRecord {
  snapshot: UsageSnapshot;
  liveDetails: ProviderLiveDetails | null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function nullableNumber(value: unknown): number | null | undefined {
  if (value === null) return null;
  return typeof value === "number" && Number.isFinite(value) && value >= 0
    ? value
    : undefined;
}

function nullableString(value: unknown): string | null | undefined {
  if (value === null) return null;
  return typeof value === "string" && value.length <= 256 ? value : undefined;
}

function isoDate(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? new Date(parsed).toISOString() : null;
}

function usageWindow(value: unknown): UsageWindow | null {
  if (!isRecord(value)) return null;
  const usedPercent = value.usedPercent;
  const remainingPercent = value.remainingPercent;
  if (
    typeof value.id !== "string" ||
    typeof value.limitId !== "string" ||
    typeof value.label !== "string" ||
    (value.kind !== "primary" && value.kind !== "secondary") ||
    typeof usedPercent !== "number" ||
    !Number.isFinite(usedPercent) ||
    usedPercent < 0 ||
    usedPercent > 100 ||
    typeof remainingPercent !== "number" ||
    !Number.isFinite(remainingPercent)
  ) {
    return null;
  }

  const limitName = nullableString(value.limitName);
  if (limitName === undefined) return null;
  const durationMinutes = nullableNumber(value.durationMinutes);
  if (durationMinutes === undefined) return null;
  const resetsAt = value.resetsAt === null ? null : isoDate(value.resetsAt);
  if (value.resetsAt !== null && resetsAt === null) return null;

  return {
    id: value.id,
    limitId: value.limitId,
    limitName,
    kind: value.kind,
    label: value.label,
    usedPercent,
    remainingPercent,
    durationMinutes,
    resetsAt,
  };
}

function liveDetails(value: unknown): ProviderLiveDetails | null | undefined {
  if (value === null) return null;
  if (!isRecord(value)) return undefined;

  const model = nullableString(value.model);
  const reasoningLevel = nullableString(value.reasoningLevel);
  const thinkingEnabled =
    value.thinkingEnabled === null || typeof value.thinkingEnabled === "boolean"
      ? value.thinkingEnabled
      : undefined;
  const inputTokens = nullableNumber(value.inputTokens);
  const outputTokens = nullableNumber(value.outputTokens);
  const cacheReadTokens = nullableNumber(value.cacheReadTokens);
  const cacheWriteTokens = nullableNumber(value.cacheWriteTokens);
  const estimatedSessionCostUsd = nullableNumber(value.estimatedSessionCostUsd);
  if (
    model === undefined ||
    reasoningLevel === undefined ||
    thinkingEnabled === undefined ||
    inputTokens === undefined ||
    outputTokens === undefined ||
    cacheReadTokens === undefined ||
    cacheWriteTokens === undefined ||
    estimatedSessionCostUsd === undefined
  ) {
    return undefined;
  }

  return {
    model,
    reasoningLevel,
    thinkingEnabled,
    inputTokens,
    outputTokens,
    cacheReadTokens,
    cacheWriteTokens,
    estimatedSessionCostUsd,
  };
}

/**
 * Rebuilds a previously cached snapshot, dropping anything that does not match
 * the contract this app wrote. The cached status is intentionally not trusted:
 * callers re-evaluate freshness against the restored `observedAt`.
 */
export function parseStoredClaudeSnapshot(
  rawValue: unknown,
): ClaudeSnapshotRecord | null {
  if (
    !isRecord(rawValue) ||
    rawValue.version !== STORE_VERSION ||
    !isRecord(rawValue.snapshot)
  ) {
    return null;
  }

  const stored = rawValue.snapshot;
  const observedAt = isoDate(stored.observedAt);
  if (
    stored.schemaVersion !== 1 ||
    stored.providerId !== "anthropic-claude" ||
    observedAt === null ||
    !Array.isArray(stored.windows)
  ) {
    return null;
  }

  const windows: UsageWindow[] = [];
  for (const entry of stored.windows) {
    const parsed = usageWindow(entry);
    if (!parsed) return null;
    windows.push(parsed);
  }

  const details = liveDetails(rawValue.liveDetails ?? null);
  if (details === undefined) return null;

  return {
    snapshot: {
      schemaVersion: 1,
      providerId: "anthropic-claude",
      providerName: "Claude",
      observedAt,
      // Freshness is a function of the clock, never of what was on disk, so
      // callers always re-evaluate this against the restored observedAt.
      status: "live",
      windows,
      bankedResets: {
        availableCount: null,
        detailsAvailable: false,
        items: [],
      },
      credits: null,
      planType: null,
      tokenUsage: null,
      message: null,
    },
    liveDetails: details,
  };
}

/**
 * Durable cache of the last Claude quota reading.
 *
 * Claude quota arrives by push and only while Claude Code is running, so
 * without this the tray would start empty after every restart and stay empty
 * until the next session happened to report in.
 */
export class ClaudeSnapshotStore {
  private writeQueue: Promise<void> = Promise.resolve();

  constructor(private readonly filePath: string) {}

  async load(): Promise<ClaudeSnapshotRecord | null> {
    try {
      const parsed = JSON.parse(
        await readFile(this.filePath, "utf8"),
      ) as unknown;
      return parseStoredClaudeSnapshot(parsed);
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === "ENOENT") {
        return null;
      }
      throw error;
    }
  }

  save(record: ClaudeSnapshotRecord): Promise<void> {
    const payload: StoredClaudeSnapshot = {
      version: STORE_VERSION,
      snapshot: record.snapshot,
      liveDetails: record.liveDetails,
    };
    const operation = this.writeQueue.then(() => this.persist(payload));
    this.writeQueue = operation.catch(() => {
      // Keep later saves usable after a failed disk write.
    });
    return operation;
  }

  private async persist(payload: StoredClaudeSnapshot): Promise<void> {
    await mkdir(dirname(this.filePath), { recursive: true });
    const temporaryPath = `${this.filePath}.${process.pid}.${randomBytes(4).toString("hex")}.tmp`;
    try {
      await writeFile(
        temporaryPath,
        `${JSON.stringify(payload, null, 2)}\n`,
        { encoding: "utf8", mode: 0o600 },
      );
      await rename(temporaryPath, this.filePath);
    } catch (error) {
      await unlink(temporaryPath).catch(() => {
        // The temporary file may not have been created.
      });
      throw error;
    }
  }
}
