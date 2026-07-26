import {
  createUnavailableSnapshot,
  markSnapshotStale,
  normalizeCodexSnapshot,
  type AppSettings,
  type UsageSnapshot,
} from "@usageapp/core";
import type { RefreshPhase } from "../shared/desktop";
import {
  CodexAppServerClient,
  errorMessage,
  looksLikeAuthenticationError,
} from "./codex-client";

export class UsageController {
  private readonly client: CodexAppServerClient;
  private snapshotValue: UsageSnapshot | null = null;
  private phaseValue: RefreshPhase = "starting";
  private errorValue: string | null = null;
  private refreshPromise: Promise<void> | null = null;
  private timer: NodeJS.Timeout | null = null;
  private notificationTimer: NodeJS.Timeout | null = null;
  private lifecycleGeneration = 0;
  private running = false;
  private settings: AppSettings;

  constructor(
    initialSettings: AppSettings,
    private readonly onChanged: () => void,
  ) {
    this.settings = initialSettings;
    this.client = new CodexAppServerClient(() => {
      this.scheduleRateLimitsRefresh();
    });
  }

  get snapshot(): UsageSnapshot | null {
    return this.snapshotValue;
  }

  get phase(): RefreshPhase {
    return this.phaseValue;
  }

  get lastError(): string | null {
    return this.errorValue;
  }

  start(): void {
    this.running = true;
    this.reschedule();
    void this.refresh();
  }

  updateSettings(nextSettings: AppSettings): void {
    const commandChanged =
      this.settings.codexCommand !== nextSettings.codexCommand;
    const intervalChanged =
      this.settings.refreshIntervalMinutes !==
      nextSettings.refreshIntervalMinutes;
    this.settings = nextSettings;
    if (commandChanged) {
      this.client.stop();
    }
    if (intervalChanged) {
      this.reschedule();
    }
  }

  async refresh(): Promise<void> {
    if (this.refreshPromise) {
      return this.refreshPromise;
    }
    this.refreshPromise = this.performRefresh();
    try {
      await this.refreshPromise;
    } finally {
      this.refreshPromise = null;
    }
  }

  stop(): void {
    this.running = false;
    this.lifecycleGeneration += 1;
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
    if (this.notificationTimer) {
      clearTimeout(this.notificationTimer);
      this.notificationTimer = null;
    }
    this.client.stop();
  }

  private async performRefresh(): Promise<void> {
    this.phaseValue = "refreshing";
    this.onChanged();
    try {
      const responses = await this.client.readUsage(
        this.settings.codexCommand,
      );
      let snapshot = normalizeCodexSnapshot(
        responses.rateLimitsResult,
        responses.usageResult,
      );
      if (responses.warnings.length > 0 && snapshot.message === null) {
        snapshot = {
          ...snapshot,
          message:
            "Rate limits are current, but detailed token history is unavailable.",
        };
      }
      this.snapshotValue = snapshot;
      this.phaseValue = "idle";
      this.errorValue = null;
    } catch (error) {
      const message = errorMessage(error);
      this.errorValue = message;
      this.phaseValue = "error";
      if (this.snapshotValue?.status === "live" || this.snapshotValue?.status === "stale") {
        this.snapshotValue = markSnapshotStale(
          this.snapshotValue,
          "Showing the last successful update because Codex could not be reached.",
        );
      } else {
        this.snapshotValue = createUnavailableSnapshot(
          looksLikeAuthenticationError(error)
            ? "auth-required"
            : "unavailable",
          looksLikeAuthenticationError(error)
            ? "Sign in with the Codex CLI, then refresh UsageApp."
            : "UsageApp could not read Codex usage.",
        );
      }
    } finally {
      this.onChanged();
    }
  }

  private reschedule(): void {
    if (this.timer) {
      clearInterval(this.timer);
    }
    const intervalMs = this.settings.refreshIntervalMinutes * 60_000;
    this.timer = setInterval(() => {
      void this.refresh();
    }, intervalMs);
    this.timer.unref();
  }

  private scheduleRateLimitsRefresh(): void {
    if (!this.running) {
      return;
    }
    if (this.notificationTimer) {
      clearTimeout(this.notificationTimer);
    }
    const generation = this.lifecycleGeneration;
    this.notificationTimer = setTimeout(() => {
      this.notificationTimer = null;
      void this.refreshAfterNotification(generation);
    }, 750);
    this.notificationTimer.unref();
  }

  private async refreshAfterNotification(generation: number): Promise<void> {
    const activeRefresh = this.refreshPromise;
    if (activeRefresh) {
      await activeRefresh;
    }
    if (!this.running || generation !== this.lifecycleGeneration) {
      return;
    }
    await this.refresh();
  }
}
