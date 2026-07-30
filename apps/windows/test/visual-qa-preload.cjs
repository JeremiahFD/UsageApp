const { contextBridge } = require("electron");

const activeProviderId =
  process.env.USAGEAPP_QA_PROVIDER === "anthropic-claude"
    ? "anthropic-claude"
    : "openai-codex";
const dates = Array.from({ length: 30 }, (_value, index) => {
  const day = String(index + 1).padStart(2, "0");
  return `2026-07-${day}`;
});
const now = "2026-07-26T14:30:00.000Z";

const codexSnapshot = {
  schemaVersion: 1,
  providerId: "openai-codex",
  providerName: "Codex",
  observedAt: now,
  status: "live",
  windows: [
    {
      id: "codex:five-hour",
      limitId: "codex",
      limitName: "Interactive",
      kind: "primary",
      label: "5-hour",
      usedPercent: 27,
      remainingPercent: 73,
      durationMinutes: 300,
      resetsAt: "2026-07-26T17:00:00.000Z",
    },
    {
      id: "codex:weekly",
      limitId: "codex",
      limitName: "Weekly",
      kind: "secondary",
      label: "Weekly",
      usedPercent: 42,
      remainingPercent: 58,
      durationMinutes: 10080,
      resetsAt: "2026-07-31T05:00:00.000Z",
    },
  ],
  bankedResets: {
    availableCount: 2,
    detailsAvailable: true,
    items: [
      {
        id: "banked-1",
        title: "Banked reset",
        description: null,
        status: "available",
        grantedAt: "2026-07-20T10:00:00.000Z",
        expiresAt: "2026-08-02T10:00:00.000Z",
      },
    ],
  },
  credits: null,
  planType: "Plus",
  tokenUsage: {
    lifetimeTokens: 18765432,
    peakDailyTokens: 912345,
    longestRunningTurnSec: 3840,
    currentStreakDays: 12,
    longestStreakDays: 23,
    dailyUsageBuckets: dates.map((date, index) => ({
      startDate: date,
      tokens: 90000 + ((index * 73811) % 620000),
    })),
  },
  message: null,
};

const claudeSnapshot = {
  schemaVersion: 1,
  providerId: "anthropic-claude",
  providerName: "Claude",
  observedAt: now,
  status: "live",
  windows: [
    {
      id: "claude:five-hour",
      limitId: "claude",
      limitName: null,
      kind: "primary",
      label: "5-hour",
      usedPercent: 36,
      remainingPercent: 64,
      durationMinutes: 300,
      resetsAt: "2026-07-26T18:00:00.000Z",
    },
    {
      id: "claude:weekly",
      limitId: "claude",
      limitName: null,
      kind: "secondary",
      label: "Weekly",
      usedPercent: 51,
      remainingPercent: 49,
      durationMinutes: 10080,
      resetsAt: "2026-08-01T06:00:00.000Z",
    },
  ],
  bankedResets: {
    availableCount: null,
    detailsAvailable: false,
    items: [],
  },
  credits: null,
  planType: "Max",
  tokenUsage: null,
  message: null,
};

const codexBuckets = dates.map((date, index) => ({
  date,
  model: null,
  reasoningLevel: null,
  inputTokens: null,
  outputTokens: null,
  cacheReadTokens: null,
  cacheWriteTokens: null,
  reasoningTokens: null,
  totalTokens: 90000 + ((index * 73811) % 620000),
  estimatedCostUsd: null,
  requestCount: null,
}));
const claudeBuckets = dates.flatMap((date, index) =>
  ["claude-sonnet-4-6", "claude-opus-4-7"].map((model, modelIndex) => {
    const inputTokens = 18000 + ((index * 4501 + modelIndex * 11000) % 48000);
    const outputTokens = 6000 + ((index * 1789 + modelIndex * 4000) % 18000);
    const cacheReadTokens = 3000 + ((index * 947) % 9000);
    const cacheWriteTokens = 1200 + ((index * 431) % 3500);
    return {
      date,
      model,
      reasoningLevel: modelIndex === 0 ? "high" : "xhigh",
      inputTokens,
      outputTokens,
      cacheReadTokens,
      cacheWriteTokens,
      reasoningTokens: null,
      totalTokens:
        inputTokens + outputTokens + cacheReadTokens + cacheWriteTokens,
      estimatedCostUsd: 0.42 + index * 0.013 + modelIndex * 0.18,
      requestCount: 4 + ((index + modelIndex) % 8),
    };
  }),
);

const state = {
  settings: {
    launchAtLogin: false,
    showWidget: false,
    startMinimized: true,
    refreshIntervalMinutes: 5,
    phoneSyncEnabled: false,
    phoneSyncPort: 47831,
    codexCommand: "auto",
    activeProviderId,
    claudeEnabled: true,
    claudeTelemetryPort: 47832,
  },
  snapshot: codexSnapshot,
  refreshPhase: "idle",
  lastError: null,
  phoneSync: {
    enabled: false,
    listening: false,
    port: 47831,
    addresses: [],
    pairedDeviceCount: 0,
    pairingCodeActive: false,
    error: null,
  },
  activeProviderId,
  providers: [
    {
      id: "openai-codex",
      name: "Codex",
      snapshot: codexSnapshot,
      analytics: {
        source: "codex-account",
        observedAt: now,
        recordingSince: dates[0],
        buckets: codexBuckets,
        capabilities: {
          dailyTotals: true,
          tokenCategories: false,
          modelFilter: false,
          reasoningFilter: false,
          estimatedCost: false,
        },
        message:
          "Codex supplies daily token totals, but not historical model or reasoning-level attribution.",
      },
      refreshPhase: "idle",
      lastError: null,
      liveDetails: null,
    },
    {
      id: "anthropic-claude",
      name: "Claude",
      snapshot: claudeSnapshot,
      analytics: {
        source: "claude-otel",
        observedAt: now,
        recordingSince: "2026-07-01T13:00:00.000Z",
        buckets: claudeBuckets,
        capabilities: {
          dailyTotals: true,
          tokenCategories: true,
          modelFilter: true,
          reasoningFilter: true,
          estimatedCost: true,
        },
        message: null,
      },
      refreshPhase: "idle",
      lastError: null,
      liveDetails: {
        model: "claude-sonnet-4-6",
        reasoningLevel: "high",
        thinkingEnabled: true,
        inputTokens: 48210,
        outputTokens: 12840,
        cacheReadTokens: 9930,
        cacheWriteTokens: 2210,
        estimatedSessionCostUsd: 1.842,
      },
    },
  ],
  claudeIntegration: {
    state: "connected",
    statusLineConnected: true,
    telemetryConnected: true,
    receiverListening: true,
    message:
      "Claude quota and local Claude Code activity monitoring are connected.",
  },
};

const bridge = {
  getState: async () => state,
  refresh: async () => state,
  updateSettings: async (patch) => {
    Object.assign(state.settings, patch);
    if (patch.activeProviderId) state.activeProviderId = patch.activeProviderId;
    return state;
  },
  createPairingCode: async () => ({
    code: "123456",
    expiresAt: now,
    addresses: [],
    port: 47831,
  }),
  revokePhoneTokens: async () => state,
  connectClaude: async () => state,
  disconnectClaude: async () => state,
  hideFlyout: async () => {},
  showFlyout: async () => {},
  showDashboard: async () => {},
  quit: async () => {},
  onStateChanged: () => () => {},
};

contextBridge.exposeInMainWorld("usageApp", bridge);
