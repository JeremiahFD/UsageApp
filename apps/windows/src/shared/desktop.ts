import type { AppSettings, UsageSnapshot } from "@usageapp/core";

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

export interface DesktopState {
  settings: AppSettings;
  snapshot: UsageSnapshot | null;
  refreshPhase: RefreshPhase;
  lastError: string | null;
  phoneSync: PhoneSyncStatus;
}

export const IPC = {
  getState: "usageapp:get-state",
  stateChanged: "usageapp:state-changed",
  refresh: "usageapp:refresh",
  updateSettings: "usageapp:update-settings",
  createPairingCode: "usageapp:create-pairing-code",
  revokePhoneTokens: "usageapp:revoke-phone-tokens",
  hideFlyout: "usageapp:hide-flyout",
  showFlyout: "usageapp:show-flyout",
  quit: "usageapp:quit",
} as const;

export interface UsageAppBridge {
  getState(): Promise<DesktopState>;
  refresh(): Promise<DesktopState>;
  updateSettings(patch: Partial<AppSettings>): Promise<DesktopState>;
  createPairingCode(): Promise<PairingCodeInfo>;
  revokePhoneTokens(): Promise<DesktopState>;
  hideFlyout(): Promise<void>;
  showFlyout(): Promise<void>;
  quit(): Promise<void>;
  onStateChanged(listener: (state: DesktopState) => void): () => void;
}

declare global {
  interface Window {
    usageApp: UsageAppBridge;
  }
}
