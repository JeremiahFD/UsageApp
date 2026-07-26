export type SnapshotStatus =
  | "live"
  | "stale"
  | "auth-required"
  | "unavailable";

export interface UsageWindow {
  id: string;
  limitId: string;
  limitName: string | null;
  kind: "primary" | "secondary";
  label: string;
  usedPercent: number;
  remainingPercent: number;
  durationMinutes: number | null;
  resetsAt: string | null;
}

export interface BankedReset {
  id: string;
  title: string | null;
  description: string | null;
  status: string;
  grantedAt: string;
  expiresAt: string | null;
}

export interface BankedResetSummary {
  /**
   * Null means the provider did not return an authoritative count. Detail rows
   * may be capped and must never be used to invent this value.
   */
  availableCount: number | null;
  detailsAvailable: boolean;
  items: BankedReset[];
}

export interface CreditSummary {
  hasCredits: boolean;
  unlimited: boolean;
  balance: string | null;
}

export interface TokenUsageDailyBucket {
  startDate: string;
  tokens: number;
}

export interface TokenUsageSummary {
  lifetimeTokens: number | null;
  peakDailyTokens: number | null;
  longestRunningTurnSec: number | null;
  currentStreakDays: number | null;
  longestStreakDays: number | null;
  dailyUsageBuckets: TokenUsageDailyBucket[] | null;
}

export interface UsageSnapshot {
  schemaVersion: 1;
  providerId: string;
  providerName: string;
  observedAt: string;
  status: SnapshotStatus;
  windows: UsageWindow[];
  bankedResets: BankedResetSummary;
  credits: CreditSummary | null;
  planType: string | null;
  tokenUsage: TokenUsageSummary | null;
  message: string | null;
}

export interface AppSettings {
  launchAtLogin: boolean;
  showWidget: boolean;
  startMinimized: boolean;
  refreshIntervalMinutes: number;
  phoneSyncEnabled: boolean;
  phoneSyncPort: number;
  /**
   * "auto" tries the installed Codex command first and the official npm fallback
   * second. Any other value is treated as an explicit executable path.
   */
  codexCommand: string;
}

export const DEFAULT_SETTINGS: AppSettings = {
  launchAtLogin: false,
  showWidget: false,
  startMinimized: true,
  refreshIntervalMinutes: 5,
  phoneSyncEnabled: false,
  phoneSyncPort: 47_831,
  codexCommand: "auto",
};

export interface PairRequest {
  code: string;
  deviceName: string;
}

export interface PairResponse {
  token: string;
  deviceId: string;
}

export interface TraySummary {
  percentage: number | null;
  tooltip: string;
  nextResetAt: string | null;
}
