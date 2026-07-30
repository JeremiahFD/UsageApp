import { join } from "node:path";

import type { AppSettings, UsageSnapshot } from "@usageapp/core";
import type {
  ClaudeIntegrationStatus,
  ProviderLiveDetails,
  RefreshPhase,
  UsageAnalytics,
} from "../shared/desktop";
import { ClaudeIntegrationManager } from "./claude-integration";
import {
  ClaudePlanUsageWatcher,
  snapshotFromClaudePlanUsage,
  type ClaudePlanUsageSample,
} from "./claude-plan-usage";
import { ClaudeSnapshotStore } from "./claude-snapshot-store";
import {
  ClaudeActivityStore,
  normalizeClaudeStatusLine,
  parseClaudeOtlpLogs,
} from "./claude-telemetry";

const CLAUDE_ACTIVITY_FILE_NAME = "claude-activity.ndjson";
const CLAUDE_SNAPSHOT_FILE_NAME = "claude-snapshot.json";
const CLAUDE_STALE_AFTER_MS = 15 * 60_000;

const UI_ERRORS = {
  start:
    "Claude monitoring could not start. Check the telemetry port and Claude settings, then try again.",
  connect:
    "Claude monitoring could not connect. Check the telemetry port and Claude settings, then try again.",
  configure:
    "Claude monitoring settings could not be applied. Your existing Claude settings were preserved where it was unsafe to change them.",
  disconnect:
    "Claude monitoring could not fully disconnect. Your existing Claude settings were preserved where it was unsafe to restore them.",
  stop: "Claude's local receiver could not stop cleanly.",
  historyLoad: "Claude activity history could not be loaded.",
  historySave: "Claude activity could not be saved.",
  statusLine: "Claude's local status update could not be read.",
  snapshotLoad: "The last Claude usage reading could not be restored.",
} as const;

/**
 * Carries reset times forward onto a snapshot that has none.
 *
 * The desktop app reports utilization without reset times, so a still-future
 * reset last seen from a Claude Code session is better than showing nothing.
 */
function withKnownResets(
  snapshot: UsageSnapshot,
  source: UsageSnapshot | null,
  nowMs: number,
): UsageSnapshot {
  if (source === null) return snapshot;
  let changed = false;
  const windows = snapshot.windows.map((window) => {
    if (window.resetsAt !== null) return window;
    const match = source.windows.find((entry) => entry.id === window.id);
    if (!match || match.resetsAt === null) return window;
    const resetAtMs = Date.parse(match.resetsAt);
    if (!Number.isFinite(resetAtMs) || resetAtMs <= nowMs) return window;
    changed = true;
    return { ...window, resetsAt: match.resetsAt };
  });
  return changed ? { ...snapshot, windows } : snapshot;
}

function reevaluateSnapshot(
  snapshot: UsageSnapshot | null,
  now = new Date(),
): UsageSnapshot | null {
  if (snapshot === null || snapshot.status !== "live") {
    return snapshot;
  }

  const nowMs = now.getTime();
  const observedAtMs = Date.parse(snapshot.observedAt);
  const tooOld =
    Number.isFinite(observedAtMs) &&
    nowMs - observedAtMs > CLAUDE_STALE_AFTER_MS;
  const resetHasPassed = snapshot.windows.some((window) => {
    if (window.resetsAt === null) return false;
    const resetAtMs = Date.parse(window.resetsAt);
    return Number.isFinite(resetAtMs) && resetAtMs <= nowMs;
  });
  if (!tooOld && !resetHasPassed) {
    return snapshot;
  }

  return {
    ...snapshot,
    status: "stale",
    message: resetHasPassed
      ? "Claude's last reported reset data has expired. Run Claude Code to refresh it."
      : "Showing the last Claude status update.",
  };
}

function sanitizedIntegrationStatus(
  status: ClaudeIntegrationStatus,
): ClaudeIntegrationStatus {
  if (status.state !== "error") {
    return { ...status };
  }
  return {
    ...status,
    message:
      "Claude monitoring encountered an error. Check the telemetry port and Claude settings, then try again.",
  };
}

/**
 * Provider-facing controller for Claude's opt-in local integration.
 *
 * Raw status-line and OTLP inputs are reduced to the normalized/sanitized
 * contracts in their callbacks and are never retained by this controller.
 */
export class ClaudeController {
  private readonly activityStore: ClaudeActivityStore;
  private readonly snapshotStore: ClaudeSnapshotStore;
  private readonly planUsageWatcher: ClaudePlanUsageWatcher;
  private readonly integrationManager: ClaudeIntegrationManager;
  private readonly onChanged: () => void;

  private settings: AppSettings;
  /** Pushed by a running Claude Code session; authoritative when fresh. */
  private snapshotValue: UsageSnapshot | null = null;
  /** Polled from the Claude desktop app; the only source that self-updates. */
  private planSnapshotValue: UsageSnapshot | null = null;
  private liveDetailsValue: ProviderLiveDetails | null = null;
  private phaseValue: RefreshPhase = "starting";
  private errorValue: string | null = null;
  private operationQueue: Promise<void> = Promise.resolve();

  constructor(
    userDataPath: string,
    initialSettings: AppSettings,
    onChanged: () => void,
  ) {
    this.settings = { ...initialSettings };
    this.onChanged = onChanged;
    this.activityStore = new ClaudeActivityStore(
      join(userDataPath, CLAUDE_ACTIVITY_FILE_NAME),
    );
    this.snapshotStore = new ClaudeSnapshotStore(
      join(userDataPath, CLAUDE_SNAPSHOT_FILE_NAME),
    );
    this.planUsageWatcher = new ClaudePlanUsageWatcher({
      onSample: (sample) => this.acceptPlanUsage(sample),
    });
    this.integrationManager = new ClaudeIntegrationManager({
      userDataPath,
      port: initialSettings.claudeTelemetryPort,
      onStatusLine: (raw) => this.acceptStatusLine(raw),
      onOtlpLogs: (raw) => this.acceptOtlpLogs(raw),
      onChanged: () => this.notifyChanged(),
    });
  }

  /**
   * Prefers a live Claude Code reading, falls back to the desktop app's
   * self-updating one, and only then to whichever stale reading exists.
   */
  get snapshot(): UsageSnapshot | null {
    const now = new Date();
    const sessionSnapshot = reevaluateSnapshot(this.snapshotValue, now);
    if (sessionSnapshot?.status === "live") {
      return sessionSnapshot;
    }

    const planSnapshot = reevaluateSnapshot(this.planSnapshotValue, now);
    if (planSnapshot?.status === "live") {
      return withKnownResets(planSnapshot, this.snapshotValue, now.getTime());
    }
    return sessionSnapshot ?? planSnapshot;
  }

  get analytics(): UsageAnalytics {
    return this.activityStore.analytics();
  }

  get phase(): RefreshPhase {
    return this.phaseValue;
  }

  get lastError(): string | null {
    return this.errorValue;
  }

  get liveDetails(): ProviderLiveDetails | null {
    return this.liveDetailsValue;
  }

  get integrationStatus(): ClaudeIntegrationStatus {
    return sanitizedIntegrationStatus(this.integrationManager.status());
  }

  start(): Promise<void> {
    return this.enqueue(async () => {
      this.phaseValue = "starting";
      this.errorValue = null;
      this.notifyChanged();

      const errors: string[] = [];
      try {
        await this.activityStore.load();
      } catch {
        errors.push(UI_ERRORS.historyLoad);
      }

      try {
        const restored = await this.snapshotStore.load();
        if (restored) {
          this.snapshotValue = restored.snapshot;
          this.liveDetailsValue = restored.liveDetails;
        }
      } catch {
        errors.push(UI_ERRORS.snapshotLoad);
      }

      this.syncPlanUsageWatcher();

      if (this.settings.claudeEnabled) {
        const status = await this.integrationManager.configure({
          enabled: true,
          port: this.settings.claudeTelemetryPort,
        });
        if (status.state === "error") {
          errors.push(UI_ERRORS.start);
        }
      }

      this.finishOperation(errors);
    });
  }

  connect(): Promise<void> {
    return this.enqueue(async () => {
      this.beginOperation();
      this.settings = {
        ...this.settings,
        claudeEnabled: true,
      };
      this.syncPlanUsageWatcher();
      const status = await this.integrationManager.configure({
        enabled: true,
        port: this.settings.claudeTelemetryPort,
      });
      this.finishOperation(
        status.state === "error" ? [UI_ERRORS.connect] : [],
      );
    });
  }

  disconnect(): Promise<void> {
    return this.enqueue(async () => {
      this.beginOperation();
      this.settings = {
        ...this.settings,
        claudeEnabled: false,
      };
      this.syncPlanUsageWatcher();
      const disconnectStatus = await this.integrationManager.disconnect();
      const stopStatus = await this.integrationManager.stop();
      this.finishOperation(
        disconnectStatus.state === "error" ||
          stopStatus.state === "error"
          ? [UI_ERRORS.disconnect]
          : [],
      );
    });
  }

  configure(nextSettings: AppSettings): Promise<void> {
    return this.enqueue(async () => {
      const previousEnabled = this.settings.claudeEnabled;
      const previousPort = this.settings.claudeTelemetryPort;
      this.settings = { ...nextSettings };
      const enabledChanged =
        previousEnabled !== nextSettings.claudeEnabled;
      const portChanged =
        previousPort !== nextSettings.claudeTelemetryPort;
      if (enabledChanged) {
        this.syncPlanUsageWatcher();
      }
      if (!enabledChanged && !portChanged) {
        return;
      }

      this.beginOperation();
      // Remaining disabled, including a disabled-only port edit, must not read
      // or modify Claude's configuration. The selected port is applied on the
      // next explicit enable/connect.
      if (!previousEnabled && !nextSettings.claudeEnabled) {
        this.finishOperation([]);
        return;
      }

      const status = await this.integrationManager.configure({
        enabled: nextSettings.claudeEnabled,
        port: nextSettings.claudeTelemetryPort,
      });
      this.finishOperation(
        status.state === "error" ? [UI_ERRORS.configure] : [],
      );
    });
  }

  /**
   * Claude Code cannot be polled, but the desktop app's record can, so this
   * re-reads that rather than only re-checking the age of what is cached.
   */
  refresh(): Promise<void> {
    return this.enqueue(async () => {
      this.phaseValue = "refreshing";
      this.notifyChanged();
      await this.planUsageWatcher.read(true);
      this.snapshotValue = reevaluateSnapshot(this.snapshotValue);
      this.phaseValue = this.errorValue === null ? "idle" : "error";
      this.notifyChanged();
    });
  }

  stop(): Promise<void> {
    return this.enqueue(async () => {
      this.planUsageWatcher.stop();
      const status = await this.integrationManager.stop();
      if (status.state === "error") {
        this.phaseValue = "error";
        this.errorValue = UI_ERRORS.stop;
      }
      this.notifyChanged();
    });
  }

  /**
   * The desktop app's record needs none of the settings changes the Claude
   * Code integration makes, but it is still Claude monitoring, so it follows
   * the same opt-in.
   */
  private syncPlanUsageWatcher(): void {
    if (this.settings.claudeEnabled) {
      this.planUsageWatcher.start();
      return;
    }
    this.planUsageWatcher.stop();
    this.planSnapshotValue = null;
    this.integrationManager.markPlanUsageReceived(null);
  }

  private beginOperation(): void {
    this.phaseValue = "refreshing";
    this.errorValue = null;
    this.notifyChanged();
  }

  private finishOperation(errors: readonly string[]): void {
    this.errorValue = errors[0] ?? null;
    this.phaseValue = this.errorValue === null ? "idle" : "error";
    this.notifyChanged();
  }

  private enqueue(operation: () => Promise<void>): Promise<void> {
    const result = this.operationQueue.then(async () => {
      try {
        await operation();
      } catch {
        this.phaseValue = "error";
        this.errorValue = UI_ERRORS.configure;
        this.notifyChanged();
      }
    });
    this.operationQueue = result.catch(() => {
      // Keep later lifecycle operations usable after an unexpected failure.
    });
    return result;
  }

  private acceptStatusLine(raw: unknown): void {
    try {
      const normalized = normalizeClaudeStatusLine(raw, {
        observedAt: new Date(),
      });
      raw = null;
      const hasQuota = normalized.snapshot.windows.length > 0;
      // A status line without rate limits carries no quota reading, so keep
      // the previous one rather than replacing it with an empty snapshot.
      if (hasQuota) {
        this.snapshotValue = normalized.snapshot;
        void this.snapshotStore
          .save({
            snapshot: normalized.snapshot,
            liveDetails: normalized.liveDetails,
          })
          .catch(() => {
            // A cache write failure must not interrupt live collection.
          });
      }
      this.liveDetailsValue = normalized.liveDetails;
      this.integrationManager.markStatusLineReceived(hasQuota);
      this.phaseValue = "idle";
      this.errorValue = null;
    } catch {
      raw = null;
      this.phaseValue = "error";
      this.errorValue = UI_ERRORS.statusLine;
    }
    this.notifyChanged();
  }

  private acceptPlanUsage(sample: ClaudePlanUsageSample | null): void {
    if (sample === null) {
      this.planSnapshotValue = null;
      this.integrationManager.markPlanUsageReceived(null);
      this.notifyChanged();
      return;
    }

    const snapshot = snapshotFromClaudePlanUsage(sample);
    this.planSnapshotValue = snapshot.windows.length > 0 ? snapshot : null;
    const observedAtMs = Date.parse(sample.observedAt);
    this.integrationManager.markPlanUsageReceived(
      this.planSnapshotValue !== null && Number.isFinite(observedAtMs)
        ? observedAtMs
        : null,
    );
    this.notifyChanged();
  }

  private async acceptOtlpLogs(raw: unknown): Promise<void> {
    let events;
    try {
      events = parseClaudeOtlpLogs(raw);
      raw = null;
      await this.activityStore.append(events);
      if (events.length > 0) {
        this.integrationManager.markTelemetryReceived();
      }
      this.phaseValue = "idle";
      this.errorValue = null;
    } catch {
      raw = null;
      this.phaseValue = "error";
      this.errorValue = UI_ERRORS.historySave;
      this.notifyChanged();
      throw new Error("Claude activity could not be recorded.");
    }
    this.notifyChanged();
  }

  private notifyChanged(): void {
    try {
      this.onChanged();
    } catch {
      // UI notification failures must not interrupt collection or restoration.
    }
  }
}
