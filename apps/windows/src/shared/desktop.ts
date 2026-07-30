import type {
  AppSettings,
  ProviderId,
  UsageSnapshot,
} from "@usageapp/core";

export type RefreshPhase = "starting" | "refreshing" | "idle" | "error";

export interface PhoneSyncStatus {
  enabled: boolean;
  listening: boolean;
  port: number;
  addresses: string[];
  pairedDeviceCount: number;
  pairingCodeActive: boolean;
  error: string | null;
}

export interface PairingCodeInfo {
  code: string;
  expiresAt: string;
  addresses: string[];
  port: number;
}

export interface UsageHistoryBucket {
  date: string;
  model: string | null;
  reasoningLevel: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  cacheReadTokens: number | null;
  cacheWriteTokens: number | null;
  reasoningTokens: number | null;
  totalTokens: number;
  estimatedCostUsd: number | null;
  requestCount: number | null;
  /** Wall-clock observation span for local telemetry only; never a quota metric. */
  observedMinutes?: number | null;
}

export interface UsageAnalyticsCapabilities {
  dailyTotals: boolean;
  tokenCategories: boolean;
  modelFilter: boolean;
  reasoningFilter: boolean;
  estimatedCost: boolean;
  tokensPerMinute: boolean;
}

export interface UsageAnalytics {
  source: "codex-account" | "claude-otel";
  observedAt: string;
  recordingSince: string | null;
  buckets: UsageHistoryBucket[];
  capabilities: UsageAnalyticsCapabilities;
  message: string | null;
}

export interface ProviderLiveDetails {
  model: string | null;
  reasoningLevel: string | null;
  thinkingEnabled: boolean | null;
  inputTokens: number | null;
  outputTokens: number | null;
  cacheReadTokens: number | null;
  cacheWriteTokens: number | null;
  estimatedSessionCostUsd: number | null;
}

export interface ProviderDesktopState {
  id: ProviderId;
  name: string;
  snapshot: UsageSnapshot | null;
  analytics: UsageAnalytics;
  refreshPhase: RefreshPhase;
  lastError: string | null;
  liveDetails: ProviderLiveDetails | null;
}

export type ClaudeIntegrationState =
  | "disconnected"
  | "awaiting-session"
  | "connected"
  | "partial"
  | "conflict"
  | "error";

export interface ClaudeIntegrationStatus {
  state: ClaudeIntegrationState;
  /** Configuration is installed; this does not mean Claude has sent data. */
  statusLineConfigured: boolean;
  telemetryConfigured: boolean;
  /** A current UsageApp process has received the corresponding signal. */
  statusLineConnected: boolean;
  telemetryConnected: boolean;
  receiverListening: boolean;
  message: string | null;
}

export interface DesktopState {
  settings: AppSettings;
  /** Codex compatibility snapshot used by the existing phone-sync contract. */
  snapshot: UsageSnapshot | null;
  refreshPhase: RefreshPhase;
  lastError: string | null;
  phoneSync: PhoneSyncStatus;
  activeProviderId: ProviderId;
  providers: ProviderDesktopState[];
  claudeIntegration: ClaudeIntegrationStatus;
}

export const IPC = {
  getState: "usageapp:get-state",
  stateChanged: "usageapp:state-changed",
  refresh: "usageapp:refresh",
  updateSettings: "usageapp:update-settings",
  createPairingCode: "usageapp:create-pairing-code",
  revokePhoneTokens: "usageapp:revoke-phone-tokens",
  connectClaude: "usageapp:connect-claude",
  disconnectClaude: "usageapp:disconnect-claude",
  hideFlyout: "usageapp:hide-flyout",
  showFlyout: "usageapp:show-flyout",
  showDashboard: "usageapp:show-dashboard",
  showTrayIconSettings: "usageapp:show-tray-icon-settings",
  setTrayIconSettingsDirty: "usageapp:set-tray-icon-settings-dirty",
  quit: "usageapp:quit",
} as const;

export interface UsageAppBridge {
  getState(): Promise<DesktopState>;
  refresh(): Promise<DesktopState>;
  updateSettings(patch: Partial<AppSettings>): Promise<DesktopState>;
  createPairingCode(): Promise<PairingCodeInfo>;
  revokePhoneTokens(): Promise<DesktopState>;
  connectClaude(): Promise<DesktopState>;
  disconnectClaude(): Promise<DesktopState>;
  hideFlyout(): Promise<void>;
  showFlyout(): Promise<void>;
  showDashboard(): Promise<void>;
  showTrayIconSettings(): Promise<void>;
  setTrayIconSettingsDirty(dirty: boolean): Promise<void>;
  quit(): Promise<void>;
  onStateChanged(listener: (state: DesktopState) => void): () => void;
}

declare global {
  interface Window {
    usageApp: UsageAppBridge;
  }
}
