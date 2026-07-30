export type SnapshotStatus =
  | "live"
  | "stale"
  | "auth-required"
  | "unavailable";

export type ProviderId = "openai-codex" | "anthropic-claude";
export type ProviderDisplayMode = "active" | "both";
export type TrayIconPreset = "classic" | "solid-letter" | "solid-percent" | "badge" | "high-contrast" | "colored-text" | "custom";
export type TrayIconShape = "circle" | "rounded-square";
export type TrayIconContent = "percent" | "provider" | "auto";
export type TrayIconFill = "solid" | "dark" | "transparent";
export type TrayIconBorder = "none" | "thin" | "thick";
export type TrayIconTextTone = "auto" | "light" | "dark" | "provider" | "custom";
/**
 * Font used only for the tiny Windows notification-area number. The first
 * three values preserve the original hand-built bitmap faces. The remaining
 * values use installed Windows font outlines and fall back to the bold bitmap
 * face if that font is unavailable.
 */
export type TrayIconFont =
  | "classic"
  | "bold"
  | "rounded"
  | "segoe-ui"
  | "verdana"
  | "tahoma"
  | "arial"
  | "trebuchet-ms"
  | "georgia"
  | "consolas";
export type InterfaceFont =
  | "system"
  | "segoe-ui"
  | "verdana"
  | "tahoma"
  | "arial"
  | "trebuchet-ms"
  | "georgia"
  | "consolas";

export interface TrayIconSavedPreset {
  id: string;
  name: string;
  shape: TrayIconShape;
  content: TrayIconContent;
  fill: TrayIconFill;
  border: TrayIconBorder;
  codexColor: string;
  claudeColor: string;
  textTone: TrayIconTextTone;
  codexTextColor: string;
  claudeTextColor: string;
  maximizeText: boolean;
  font: TrayIconFont;
}

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
  /**
   * Provider-formatted balance. Kept as a string because the provider owns the
   * currency and precision; UsageApp must never reinterpret it as a number.
   */
  balance: string | null;
  /**
   * True when a spending control has stopped further paid usage. Null when the
   * provider did not say. This lives beside credits rather than inside the
   * provider's credits object, but it is what a spending limit looks like to
   * someone reading the panel.
   */
  spendControlReached: boolean | null;
  /** Which limit the account ran into, when the provider names one. */
  rateLimitReachedType: string | null;
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
  activeProviderId: ProviderId;
  claudeEnabled: boolean;
  claudeTelemetryPort: number;
  /** Extra dashboard scale on top of Windows display scaling. */
  dashboardTextScale: 100 | 125 | 150 | 175;
  /** Extra scale for the taskbar-side flyout. */
  flyoutTextScale: 100 | 125 | 150 | 175;
  /** Extra scale for the bottom-right compact widget. */
  widgetTextScale: 100 | 125 | 150 | 175;
  /** Font used by every Windows surface except the taskbar notification icon. */
  interfaceFont: InterfaceFont;
  /** One active tray icon, or one labelled icon for each provider. */
  trayProviderDisplay: ProviderDisplayMode;
  trayIconPreset: TrayIconPreset;
  trayIconShape: TrayIconShape;
  trayIconContent: TrayIconContent;
  trayIconFill: TrayIconFill;
  trayIconBorder: TrayIconBorder;
  trayIconCodexColor: string;
  trayIconClaudeColor: string;
  trayIconTextTone: TrayIconTextTone;
  trayIconCodexTextColor: string;
  trayIconClaudeTextColor: string;
  trayIconMaximizeText: boolean;
  /** Font used only for the number or provider letter inside the tray icon. */
  trayIconFont: TrayIconFont;
  trayIconSavedPresets: TrayIconSavedPreset[];
  trayIconActiveSavedPresetId: string | null;
  /** One active compact widget readout, or a readout for each provider. */
  widgetProviderDisplay: ProviderDisplayMode;
  usageAlertsEnabled: boolean;
  /** Remaining-percent thresholds, sorted high to low. */
  usageAlertThresholds: number[];
}

export const DEFAULT_SETTINGS: AppSettings = {
  launchAtLogin: false,
  showWidget: false,
  startMinimized: true,
  refreshIntervalMinutes: 5,
  phoneSyncEnabled: false,
  phoneSyncPort: 47_831,
  codexCommand: "auto",
  activeProviderId: "openai-codex",
  claudeEnabled: false,
  claudeTelemetryPort: 47_832,
  dashboardTextScale: 125,
  flyoutTextScale: 125,
  widgetTextScale: 125,
  interfaceFont: "system",
  trayProviderDisplay: "active",
  trayIconPreset: "classic",
  trayIconShape: "circle",
  trayIconContent: "auto",
  trayIconFill: "dark",
  trayIconBorder: "thick",
  trayIconCodexColor: "#38bdf8",
  trayIconClaudeColor: "#e89a62",
  trayIconTextTone: "auto",
  trayIconCodexTextColor: "#38bdf8",
  trayIconClaudeTextColor: "#e89a62",
  trayIconMaximizeText: false,
  trayIconFont: "classic",
  trayIconSavedPresets: [],
  trayIconActiveSavedPresetId: null,
  widgetProviderDisplay: "active",
  usageAlertsEnabled: false,
  usageAlertThresholds: [25, 10],
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
