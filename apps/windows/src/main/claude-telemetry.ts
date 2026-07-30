import { createHash } from "node:crypto";
import { appendFile, mkdir, readFile } from "node:fs/promises";
import { dirname } from "node:path";

import type { UsageSnapshot, UsageWindow } from "@usageapp/core";
import type {
  ProviderLiveDetails,
  UsageAnalytics,
  UsageAnalyticsCapabilities,
  UsageHistoryBucket,
} from "../shared/desktop";

const CLAUDE_PROVIDER_ID = "anthropic-claude";
const CLAUDE_PROVIDER_NAME = "Claude";
const CLAUDE_API_REQUEST_EVENT = "claude_code.api_request";
const CLAUDE_STALE_AFTER_MS = 15 * 60_000;
const PERSISTED_SCHEMA_VERSION = 1;

const CLAUDE_EFFORT_LEVELS = new Set([
  "low",
  "medium",
  "high",
  "xhigh",
  "max",
]);

const CLAUDE_SERVICE_NAMES = new Set([
  "claude-code",
  "claude-code-desktop",
]);

const PERSISTED_EVENT_KEYS = new Set([
  "schemaVersion",
  "eventId",
  "timestamp",
  "sequence",
  "requestId",
  "model",
  "effort",
  "inputTokens",
  "outputTokens",
  "cacheReadTokens",
  "cacheCreationTokens",
  "costUsd",
]);

export const CLAUDE_ANALYTICS_CAPABILITIES: Readonly<UsageAnalyticsCapabilities> =
  Object.freeze({
    dailyTotals: true,
    tokenCategories: true,
    modelFilter: true,
    reasoningFilter: true,
    estimatedCost: true,
    tokensPerMinute: true,
  });

export interface ClaudeStatusLineOptions {
  /**
   * Time at which UsageApp received this status-line payload. Claude's
   * documented payload has no observation timestamp, so callers must retain
   * this value when caching the normalized result.
   */
  observedAt?: Date | string;
  /** Current time, injectable for deterministic stale-state checks. */
  now?: Date | string;
  staleAfterMs?: number;
}

export interface ClaudeStatusLineNormalization {
  snapshot: UsageSnapshot;
  liveDetails: ProviderLiveDetails;
}

/**
 * Sanitized representation of one documented `claude_code.api_request`
 * event. It intentionally has no session, user, account, organization,
 * prompt, response, tool, file, or raw-payload field.
 */
export interface ClaudeApiRequestEvent {
  eventId: string;
  timestamp: string;
  sequence: number | null;
  requestId: string | null;
  model: string | null;
  effort: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  cacheReadTokens: number | null;
  cacheCreationTokens: number | null;
  costUsd: number | null;
}

export interface ClaudeActivityLoadResult {
  loaded: number;
  ignored: number;
}

export interface ClaudeActivityIngestResult {
  parsed: number;
  appended: number;
}

type JsonObject = Record<string, unknown>;

interface MutableUsageBucket {
  date: string;
  model: string | null;
  reasoningLevel: string | null;
  inputTokens: number;
  inputTokensAvailable: boolean;
  outputTokens: number;
  outputTokensAvailable: boolean;
  cacheReadTokens: number;
  cacheReadTokensAvailable: boolean;
  cacheWriteTokens: number;
  cacheWriteTokensAvailable: boolean;
  totalTokens: number;
  estimatedCostUsd: number;
  estimatedCostAvailable: boolean;
  requestCount: number;
  firstTimestamp: number | null;
  lastTimestamp: number | null;
}

function isObject(value: unknown): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function parseJsonInput(value: unknown): unknown {
  if (typeof value === "string") {
    try {
      return JSON.parse(value) as unknown;
    } catch {
      return null;
    }
  }
  if (Buffer.isBuffer(value) || value instanceof Uint8Array) {
    try {
      return JSON.parse(Buffer.from(value).toString("utf8")) as unknown;
    } catch {
      return null;
    }
  }
  return value;
}

function objectAt(value: unknown, key: string): JsonObject | null {
  if (!isObject(value)) return null;
  const nested = value[key];
  return isObject(nested) ? nested : null;
}

function finiteNonnegativeNumber(value: unknown): number | null {
  return typeof value === "number" &&
    Number.isFinite(value) &&
    value >= 0
    ? value
    : null;
}

function nonnegativeInteger(value: unknown): number | null {
  const number = finiteNonnegativeNumber(value);
  return number !== null && Number.isSafeInteger(number) ? number : null;
}

function percent(value: unknown): number | null {
  const number = finiteNonnegativeNumber(value);
  return number !== null && number <= 100 ? number : null;
}

function boundedString(
  value: unknown,
  maximumLength = 256,
): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized.length > 0 && normalized.length <= maximumLength
    ? normalized
    : null;
}

function safeIdentifier(
  value: unknown,
  maximumLength = 256,
): string | null {
  const text = boundedString(value, maximumLength);
  if (!text) return null;
  return /^[A-Za-z0-9][A-Za-z0-9._:@/+~-]*$/.test(text) ? text : null;
}

function effortLevel(value: unknown): string | null {
  const effort = boundedString(value, 32)?.toLowerCase() ?? null;
  return effort && CLAUDE_EFFORT_LEVELS.has(effort) ? effort : null;
}

function epochSecondsToIso(value: unknown): string | null {
  const seconds = finiteNonnegativeNumber(value);
  if (seconds === null || seconds <= 0) return null;
  const milliseconds = seconds * 1_000;
  if (!Number.isFinite(milliseconds)) return null;
  const date = new Date(milliseconds);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function validDate(value: Date | string, label: string): Date {
  const date = value instanceof Date ? new Date(value.getTime()) : new Date(value);
  if (Number.isNaN(date.getTime())) {
    throw new RangeError(`${label} must be a valid date.`);
  }
  return date;
}

function statusLineWindow(
  rawWindow: unknown,
  descriptor: {
    id: string;
    kind: "primary" | "secondary";
    label: string;
    durationMinutes: number;
  },
): UsageWindow | null {
  if (!isObject(rawWindow)) return null;
  const usedPercent = percent(rawWindow.used_percentage);
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
    resetsAt: epochSecondsToIso(rawWindow.resets_at),
  };
}

/**
 * Normalizes Claude Code's documented status-line stdin contract without
 * retaining its session ID, transcript path, cwd, or other session metadata.
 */
export function normalizeClaudeStatusLine(
  rawValue: unknown,
  options: ClaudeStatusLineOptions = {},
): ClaudeStatusLineNormalization {
  const now = validDate(options.now ?? new Date(), "now");
  const observedAt = validDate(options.observedAt ?? now, "observedAt");
  const staleAfterMs =
    typeof options.staleAfterMs === "number" &&
    Number.isFinite(options.staleAfterMs) &&
    options.staleAfterMs >= 0
      ? options.staleAfterMs
      : CLAUDE_STALE_AFTER_MS;
  const value = parseJsonInput(rawValue);
  const root = isObject(value) ? value : {};
  const rateLimits = objectAt(root, "rate_limits");
  const windows: UsageWindow[] = [];

  const fiveHour = statusLineWindow(rateLimits?.five_hour, {
    id: "claude:five-hour",
    kind: "primary",
    label: "5-hour",
    durationMinutes: 300,
  });
  if (fiveHour) windows.push(fiveHour);

  const sevenDay = statusLineWindow(rateLimits?.seven_day, {
    id: "claude:seven-day",
    kind: "secondary",
    label: "Weekly",
    durationMinutes: 10_080,
  });
  if (sevenDay) windows.push(sevenDay);

  const tooOld = now.getTime() - observedAt.getTime() > staleAfterMs;
  const resetHasPassed = windows.some((window) => {
    if (!window.resetsAt) return false;
    const resetTime = Date.parse(window.resetsAt);
    return Number.isFinite(resetTime) && resetTime <= now.getTime();
  });

  let status: UsageSnapshot["status"];
  let message: string | null;
  if (windows.length === 0) {
    status = "unavailable";
    message =
      "Claude has not provided subscription rate-limit data for this session.";
  } else if (tooOld || resetHasPassed) {
    status = "stale";
    message = resetHasPassed
      ? "Claude's last reported reset data has expired. Run Claude Code to refresh it."
      : "Showing the last Claude status update.";
  } else {
    status = "live";
    message = null;
  }

  const model = objectAt(root, "model");
  const effort = objectAt(root, "effort");
  const thinking = objectAt(root, "thinking");
  const cost = objectAt(root, "cost");
  const contextWindow = objectAt(root, "context_window");
  const currentUsage = objectAt(contextWindow, "current_usage");

  const modelId =
    safeIdentifier(model?.id, 160) ??
    boundedString(model?.display_name, 160);

  const liveDetails: ProviderLiveDetails = {
    model: modelId,
    reasoningLevel: effortLevel(effort?.level),
    thinkingEnabled:
      typeof thinking?.enabled === "boolean" ? thinking.enabled : null,
    inputTokens: nonnegativeInteger(currentUsage?.input_tokens),
    outputTokens: nonnegativeInteger(currentUsage?.output_tokens),
    cacheReadTokens: nonnegativeInteger(
      currentUsage?.cache_read_input_tokens,
    ),
    cacheWriteTokens: nonnegativeInteger(
      currentUsage?.cache_creation_input_tokens,
    ),
    estimatedSessionCostUsd: finiteNonnegativeNumber(cost?.total_cost_usd),
  };

  return {
    snapshot: {
      schemaVersion: 1,
      providerId: CLAUDE_PROVIDER_ID,
      providerName: CLAUDE_PROVIDER_NAME,
      observedAt: observedAt.toISOString(),
      status,
      windows,
      bankedResets: {
        availableCount: null,
        detailsAvailable: false,
        items: [],
      },
      credits: null,
      planType: null,
      tokenUsage: null,
      message,
    },
    liveDetails,
  };
}

function anyValue(value: unknown): unknown {
  if (!isObject(value)) return value;
  if ("stringValue" in value) return value.stringValue;
  if ("intValue" in value) return value.intValue;
  if ("doubleValue" in value) return value.doubleValue;
  if ("boolValue" in value) return value.boolValue;
  return null;
}

function attributeValue(
  containers: unknown[],
  key: string,
): unknown {
  for (const container of containers) {
    if (!isObject(container) || !Array.isArray(container.attributes)) {
      continue;
    }
    for (const attribute of container.attributes) {
      if (!isObject(attribute) || attribute.key !== key) continue;
      return anyValue(attribute.value);
    }
  }
  return null;
}

function numericAttribute(value: unknown): number | null {
  const primitive = anyValue(value);
  if (typeof primitive === "number") {
    return Number.isFinite(primitive) && primitive >= 0 ? primitive : null;
  }
  if (
    typeof primitive === "string" &&
    /^(?:0|[1-9]\d*)(?:\.\d+)?$/.test(primitive)
  ) {
    const number = Number(primitive);
    return Number.isFinite(number) ? number : null;
  }
  return null;
}

function integerAttribute(value: unknown): number | null {
  const number = numericAttribute(value);
  return number !== null && Number.isSafeInteger(number) ? number : null;
}

function isoTimestamp(value: unknown): string | null {
  const text = boundedString(anyValue(value), 80);
  if (!text) return null;
  const date = new Date(text);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function unixNanosToIso(value: unknown): string | null {
  const primitive = anyValue(value);
  if (
    (typeof primitive !== "string" && typeof primitive !== "number") ||
    !/^\d+$/.test(String(primitive))
  ) {
    return null;
  }
  try {
    const milliseconds = BigInt(String(primitive)) / 1_000_000n;
    const number = Number(milliseconds);
    if (!Number.isSafeInteger(number)) return null;
    const date = new Date(number);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  } catch {
    return null;
  }
}

function eventTimestamp(
  record: JsonObject,
  containers: unknown[],
): string | null {
  return (
    isoTimestamp(attributeValue(containers, "event.timestamp")) ??
    unixNanosToIso(record.timeUnixNano) ??
    unixNanosToIso(record.observedTimeUnixNano)
  );
}

function isClaudeApiRequest(
  record: JsonObject,
  containers: unknown[],
): boolean {
  const body = boundedString(anyValue(record.body), 128);
  const explicitEventName = boundedString(record.eventName, 128);
  const attributeEventName = boundedString(
    attributeValue(containers, "event.name"),
    128,
  );
  if (
    body === CLAUDE_API_REQUEST_EVENT ||
    explicitEventName === CLAUDE_API_REQUEST_EVENT ||
    attributeEventName === CLAUDE_API_REQUEST_EVENT
  ) {
    return true;
  }

  const serviceName = boundedString(
    attributeValue(containers, "service.name"),
    128,
  );
  return (
    CLAUDE_SERVICE_NAMES.has(serviceName ?? "") &&
    (body === "api_request" || attributeEventName === "api_request")
  );
}

function canonicalEventId(
  sessionId: string | null,
  event: Omit<ClaudeApiRequestEvent, "eventId">,
): string {
  const canonical = JSON.stringify([
    CLAUDE_API_REQUEST_EVENT,
    sessionId,
    event.requestId,
    event.timestamp,
    event.sequence,
    event.model,
    event.effort,
    event.inputTokens,
    event.outputTokens,
    event.cacheReadTokens,
    event.cacheCreationTokens,
    event.costUsd,
  ]);
  return createHash("sha256").update(canonical, "utf8").digest("hex");
}

function parseLogRecord(
  recordValue: unknown,
  scope: unknown,
  resource: unknown,
): ClaudeApiRequestEvent | null {
  if (!isObject(recordValue)) return null;
  const containers = [recordValue, scope, resource];
  if (!isClaudeApiRequest(recordValue, containers)) return null;

  const timestamp = eventTimestamp(recordValue, containers);
  if (!timestamp) return null;

  const rawModel = attributeValue(containers, "model");
  const model = safeIdentifier(rawModel, 160);
  const effort = effortLevel(attributeValue(containers, "effort"));
  const inputTokens = integerAttribute(
    attributeValue(containers, "input_tokens"),
  );
  const outputTokens = integerAttribute(
    attributeValue(containers, "output_tokens"),
  );
  const cacheReadTokens = integerAttribute(
    attributeValue(containers, "cache_read_tokens"),
  );
  const cacheCreationTokens = integerAttribute(
    attributeValue(containers, "cache_creation_tokens"),
  );
  const costUsd = numericAttribute(
    attributeValue(containers, "cost_usd"),
  );

  // A marker-only record cannot contribute to usage analytics. Dropping it
  // also avoids creating durable identifiers for malformed or spoofed logs.
  if (
    model === null &&
    inputTokens === null &&
    outputTokens === null &&
    cacheReadTokens === null &&
    cacheCreationTokens === null &&
    costUsd === null
  ) {
    return null;
  }

  const requestId =
    safeIdentifier(attributeValue(containers, "request_id"), 256) ??
    safeIdentifier(
      attributeValue(containers, "client_request_id"),
      256,
    );
  const sequence = integerAttribute(
    attributeValue(containers, "event.sequence"),
  );
  const sessionId = safeIdentifier(
    attributeValue(containers, "session.id"),
    256,
  );

  const eventWithoutId: Omit<ClaudeApiRequestEvent, "eventId"> = {
    timestamp,
    sequence,
    requestId,
    model,
    effort,
    inputTokens,
    outputTokens,
    cacheReadTokens,
    cacheCreationTokens,
    costUsd,
  };

  return {
    eventId: canonicalEventId(sessionId, eventWithoutId),
    ...eventWithoutId,
  };
}

/**
 * Parses an OTLP HTTP/JSON logs request and returns only sanitized
 * `claude_code.api_request` events. Resource identity, prompt/response bodies,
 * filesystem data, and every non-whitelisted attribute are discarded before
 * this function returns.
 */
export function parseClaudeOtlpLogs(
  rawPayload: unknown,
): ClaudeApiRequestEvent[] {
  const payload = parseJsonInput(rawPayload);
  if (!isObject(payload) || !Array.isArray(payload.resourceLogs)) {
    return [];
  }

  const events: ClaudeApiRequestEvent[] = [];
  for (const resourceLogValue of payload.resourceLogs) {
    if (!isObject(resourceLogValue)) continue;
    const resource = resourceLogValue.resource;
    if (!Array.isArray(resourceLogValue.scopeLogs)) continue;

    for (const scopeLogValue of resourceLogValue.scopeLogs) {
      if (!isObject(scopeLogValue) || !Array.isArray(scopeLogValue.logRecords)) {
        continue;
      }
      const scope = scopeLogValue.scope;
      for (const record of scopeLogValue.logRecords) {
        const event = parseLogRecord(record, scope, resource);
        if (event) events.push(event);
      }
    }
  }
  return events;
}

function persistedEvent(value: unknown): ClaudeApiRequestEvent | null {
  if (!isObject(value)) return null;
  if (Object.keys(value).some((key) => !PERSISTED_EVENT_KEYS.has(key))) {
    return null;
  }
  if (
    value.schemaVersion !== PERSISTED_SCHEMA_VERSION ||
    typeof value.eventId !== "string" ||
    !/^[a-f0-9]{64}$/.test(value.eventId)
  ) {
    return null;
  }

  const timestamp = isoTimestamp(value.timestamp);
  if (!timestamp) return null;
  const nullableInteger = (candidate: unknown): number | null | undefined => {
    if (candidate === null) return null;
    const number = nonnegativeInteger(candidate);
    return number === null ? undefined : number;
  };
  const nullableCost = (candidate: unknown): number | null | undefined => {
    if (candidate === null) return null;
    const number = finiteNonnegativeNumber(candidate);
    return number === null ? undefined : number;
  };
  const sequence = nullableInteger(value.sequence);
  const inputTokens = nullableInteger(value.inputTokens);
  const outputTokens = nullableInteger(value.outputTokens);
  const cacheReadTokens = nullableInteger(value.cacheReadTokens);
  const cacheCreationTokens = nullableInteger(value.cacheCreationTokens);
  const costUsd = nullableCost(value.costUsd);
  if (
    sequence === undefined ||
    inputTokens === undefined ||
    outputTokens === undefined ||
    cacheReadTokens === undefined ||
    cacheCreationTokens === undefined ||
    costUsd === undefined
  ) {
    return null;
  }

  const requestId =
    value.requestId === null
      ? null
      : safeIdentifier(value.requestId, 256);
  const model =
    value.model === null ? null : safeIdentifier(value.model, 160);
  const effort =
    value.effort === null ? null : effortLevel(value.effort);
  if (
    (value.requestId !== null && requestId === null) ||
    (value.model !== null && model === null) ||
    (value.effort !== null && effort === null)
  ) {
    return null;
  }

  return {
    eventId: value.eventId,
    timestamp,
    sequence,
    requestId,
    model,
    effort,
    inputTokens,
    outputTokens,
    cacheReadTokens,
    cacheCreationTokens,
    costUsd,
  };
}

function serializedEvent(event: ClaudeApiRequestEvent): string {
  return JSON.stringify({
    schemaVersion: PERSISTED_SCHEMA_VERSION,
    eventId: event.eventId,
    timestamp: event.timestamp,
    sequence: event.sequence,
    requestId: event.requestId,
    model: event.model,
    effort: event.effort,
    inputTokens: event.inputTokens,
    outputTokens: event.outputTokens,
    cacheReadTokens: event.cacheReadTokens,
    cacheCreationTokens: event.cacheCreationTokens,
    costUsd: event.costUsd,
  });
}

function localDateKey(timestamp: string): string | null {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) return null;
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function addNullableMetric(
  bucket: MutableUsageBucket,
  metric:
    | "inputTokens"
    | "outputTokens"
    | "cacheReadTokens"
    | "cacheWriteTokens",
  availableMetric:
    | "inputTokensAvailable"
    | "outputTokensAvailable"
    | "cacheReadTokensAvailable"
    | "cacheWriteTokensAvailable",
  value: number | null,
): void {
  if (value === null) return;
  bucket[metric] += value;
  bucket[availableMetric] = true;
  bucket.totalTokens += value;
}

export function createEmptyClaudeAnalytics(
  now: Date | string = new Date(),
): UsageAnalytics {
  const observedAt = validDate(now, "now").toISOString();
  return {
    source: "claude-otel",
    observedAt,
    recordingSince: null,
    buckets: [],
    capabilities: { ...CLAUDE_ANALYTICS_CAPABILITIES },
    message: "No Claude API request telemetry has been recorded yet.",
  };
}

export function aggregateClaudeActivity(
  events: readonly ClaudeApiRequestEvent[],
  now: Date | string = new Date(),
): UsageAnalytics {
  if (events.length === 0) return createEmptyClaudeAnalytics(now);

  const groups = new Map<string, MutableUsageBucket>();
  let recordingSince: string | null = null;

  for (const event of events) {
    const date = localDateKey(event.timestamp);
    if (!date) continue;
    if (recordingSince === null || event.timestamp < recordingSince) {
      recordingSince = event.timestamp;
    }
    const key = JSON.stringify([date, event.model, event.effort]);
    let bucket = groups.get(key);
    if (!bucket) {
      bucket = {
        date,
        model: event.model,
        reasoningLevel: event.effort,
        inputTokens: 0,
        inputTokensAvailable: false,
        outputTokens: 0,
        outputTokensAvailable: false,
        cacheReadTokens: 0,
        cacheReadTokensAvailable: false,
        cacheWriteTokens: 0,
        cacheWriteTokensAvailable: false,
        totalTokens: 0,
        estimatedCostUsd: 0,
        estimatedCostAvailable: false,
        requestCount: 0,
        firstTimestamp: null,
        lastTimestamp: null,
      };
      groups.set(key, bucket);
    }

    addNullableMetric(
      bucket,
      "inputTokens",
      "inputTokensAvailable",
      event.inputTokens,
    );
    addNullableMetric(
      bucket,
      "outputTokens",
      "outputTokensAvailable",
      event.outputTokens,
    );
    addNullableMetric(
      bucket,
      "cacheReadTokens",
      "cacheReadTokensAvailable",
      event.cacheReadTokens,
    );
    addNullableMetric(
      bucket,
      "cacheWriteTokens",
      "cacheWriteTokensAvailable",
      event.cacheCreationTokens,
    );
    if (event.costUsd !== null) {
      bucket.estimatedCostUsd += event.costUsd;
      bucket.estimatedCostAvailable = true;
    }
    bucket.requestCount += 1;
    const timestamp = Date.parse(event.timestamp);
    if (Number.isFinite(timestamp)) {
      bucket.firstTimestamp = bucket.firstTimestamp === null ? timestamp : Math.min(bucket.firstTimestamp, timestamp);
      bucket.lastTimestamp = bucket.lastTimestamp === null ? timestamp : Math.max(bucket.lastTimestamp, timestamp);
    }
  }

  const buckets: UsageHistoryBucket[] = [...groups.values()]
    .map((bucket) => ({
      date: bucket.date,
      model: bucket.model,
      reasoningLevel: bucket.reasoningLevel,
      inputTokens: bucket.inputTokensAvailable ? bucket.inputTokens : null,
      outputTokens: bucket.outputTokensAvailable ? bucket.outputTokens : null,
      cacheReadTokens: bucket.cacheReadTokensAvailable
        ? bucket.cacheReadTokens
        : null,
      cacheWriteTokens: bucket.cacheWriteTokensAvailable
        ? bucket.cacheWriteTokens
        : null,
      reasoningTokens: null,
      totalTokens: bucket.totalTokens,
      estimatedCostUsd: bucket.estimatedCostAvailable
        ? bucket.estimatedCostUsd
        : null,
      requestCount: bucket.requestCount,
      observedMinutes: bucket.firstTimestamp !== null && bucket.lastTimestamp !== null && bucket.lastTimestamp > bucket.firstTimestamp
        ? (bucket.lastTimestamp - bucket.firstTimestamp) / 60_000
        : null,
    }))
    .sort(
      (left, right) =>
        left.date.localeCompare(right.date) ||
        (left.model ?? "").localeCompare(right.model ?? "") ||
        (left.reasoningLevel ?? "").localeCompare(
          right.reasoningLevel ?? "",
        ),
    );

  const observedAt = validDate(now, "now").toISOString();
  return {
    source: "claude-otel",
    observedAt,
    recordingSince,
    buckets,
    capabilities: { ...CLAUDE_ANALYTICS_CAPABILITIES },
    message:
      buckets.length > 0
        ? null
        : "No valid Claude API request telemetry has been recorded yet.",
  };
}

/**
 * Append-only, sanitized Claude activity history. A single UsageApp process is
 * the intended writer; operations are serialized in-process to prevent
 * overlapping appends.
 */
export class ClaudeActivityStore {
  private readonly events = new Map<string, ClaudeApiRequestEvent>();
  private loaded = false;
  private operationQueue: Promise<void> = Promise.resolve();

  constructor(private readonly filePath: string) {}

  get size(): number {
    return this.events.size;
  }

  load(): Promise<ClaudeActivityLoadResult> {
    return this.enqueue(async () => this.loadUnlocked());
  }

  append(events: readonly ClaudeApiRequestEvent[]): Promise<number> {
    return this.enqueue(async () => {
      if (!this.loaded) {
        await this.loadUnlocked();
      }

      const additions: ClaudeApiRequestEvent[] = [];
      for (const candidate of events) {
        const event = persistedEvent({
          schemaVersion: PERSISTED_SCHEMA_VERSION,
          ...candidate,
        });
        if (!event || this.events.has(event.eventId)) continue;
        additions.push(event);
        this.events.set(event.eventId, event);
      }
      if (additions.length === 0) return 0;

      try {
        await mkdir(dirname(this.filePath), { recursive: true });
        const body = `${additions.map(serializedEvent).join("\n")}\n`;
        await appendFile(this.filePath, body, {
          encoding: "utf8",
          flag: "a",
        });
      } catch (error) {
        for (const event of additions) {
          this.events.delete(event.eventId);
        }
        throw error;
      }
      return additions.length;
    });
  }

  async ingestOtlp(rawPayload: unknown): Promise<ClaudeActivityIngestResult> {
    const events = parseClaudeOtlpLogs(rawPayload);
    const appended = await this.append(events);
    return { parsed: events.length, appended };
  }

  analytics(now: Date | string = new Date()): UsageAnalytics {
    return aggregateClaudeActivity([...this.events.values()], now);
  }

  private enqueue<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.operationQueue.then(operation);
    this.operationQueue = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }

  private async loadUnlocked(): Promise<ClaudeActivityLoadResult> {
    if (this.loaded) {
      return { loaded: this.events.size, ignored: 0 };
    }

    let content: string;
    try {
      content = await readFile(this.filePath, "utf8");
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === "ENOENT") {
        this.loaded = true;
        return { loaded: 0, ignored: 0 };
      }
      throw error;
    }

    let ignored = 0;
    for (const line of content.split(/\r?\n/)) {
      if (line.trim().length === 0) continue;
      try {
        const event = persistedEvent(JSON.parse(line) as unknown);
        if (!event) {
          ignored += 1;
          continue;
        }
        this.events.set(event.eventId, event);
      } catch {
        ignored += 1;
      }
    }
    this.loaded = true;
    return { loaded: this.events.size, ignored };
  }
}
