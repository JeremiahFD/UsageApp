import {
  app,
  BrowserWindow,
  ipcMain,
  Menu,
  Notification,
  powerMonitor,
  screen,
  Tray,
  type IpcMainInvokeEvent,
  type MenuItemConstructorOptions,
} from "electron";
import {
  DEFAULT_SETTINGS,
  summarizeForTray,
  type AppSettings,
} from "@usageapp/core";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import {
  IPC,
  type ClaudeIntegrationStatus,
  type DesktopState,
  type PairingCodeInfo,
  type ProviderDesktopState,
  type UsageAnalytics,
} from "../shared/desktop";
import { ClaudeController } from "./claude-controller";
import { PhoneSyncServer } from "./phone-sync-server";
import { SettingsStore } from "./settings-store";
import { createTrayIcon } from "./tray-icon";
import { UsageController } from "./usage-controller";

const FLYOUT_WIDTH = 410;
const FLYOUT_HEIGHT = 650;
const WIDGET_WIDTH = 320;
const WIDGET_HEIGHT = 146;
const WIDGET_HEIGHT_BOTH = 176;
const DASHBOARD_WIDTH = 1_180;
const DASHBOARD_HEIGHT = 800;
const WINDOW_MARGIN = 12;

let tray: Tray | null = null;
let claudeTray: Tray | null = null;
let flyoutWindow: BrowserWindow | null = null;
let widgetWindow: BrowserWindow | null = null;
let dashboardWindow: BrowserWindow | null = null;
let trayIconSettingsWindow: BrowserWindow | null = null;
let settingsStore: SettingsStore | null = null;
let usageController: UsageController | null = null;
let claudeController: ClaudeController | null = null;
let phoneSyncServer: PhoneSyncServer | null = null;
let isQuitting = false;
let trayInteractionUntil = 0;
let phoneSyncBindingTimer: NodeJS.Timeout | null = null;
let settingsApplyQueue: Promise<void> = Promise.resolve();
const alertedThresholds = new Map<string, { resetAt: string | null; remaining: number; thresholds: Set<number> }>();

const launchedHidden = process.argv.includes("--hidden");
const hasSingleInstanceLock = app.requestSingleInstanceLock();

function currentSettings(): AppSettings {
  return settingsStore?.get() ?? { ...DEFAULT_SETTINGS };
}

function trayIconStyle(settings: AppSettings) {
  return {
    preset: settings.trayIconPreset,
    shape: settings.trayIconShape,
    content: settings.trayIconContent,
    fill: settings.trayIconFill,
    border: settings.trayIconBorder,
    codexColor: settings.trayIconCodexColor,
    claudeColor: settings.trayIconClaudeColor,
    textTone: settings.trayIconTextTone,
    codexTextColor: settings.trayIconCodexTextColor,
    claudeTextColor: settings.trayIconClaudeTextColor,
    maximizeText: settings.trayIconMaximizeText,
    font: settings.trayIconFont,
  } as const;
}

function emptyClaudeAnalytics(): UsageAnalytics {
  return {
    source: "claude-otel",
    observedAt: new Date().toISOString(),
    recordingSince: null,
    buckets: [],
    capabilities: {
      dailyTotals: false,
      tokenCategories: false,
      modelFilter: false,
      reasoningFilter: false,
      estimatedCost: false,
      tokensPerMinute: false,
    },
    message:
      "Connect Claude to begin recording local Claude Code activity. Historical data starts after connection.",
  };
}

function disconnectedClaudeStatus(): ClaudeIntegrationStatus {
  return {
    state: "disconnected",
    statusLineConfigured: false,
    telemetryConfigured: false,
    statusLineConnected: false,
    telemetryConnected: false,
    receiverListening: false,
    message: "Claude monitoring is not connected.",
  };
}

function currentState(): DesktopState {
  const settings = currentSettings();
  const codexProvider: ProviderDesktopState = {
    id: "openai-codex",
    name: "Codex",
    snapshot: usageController?.snapshot ?? null,
    analytics:
      usageController?.analytics ?? {
        source: "codex-account",
        observedAt: new Date().toISOString(),
        recordingSince: null,
        buckets: [],
        capabilities: {
          dailyTotals: false,
          tokenCategories: false,
          modelFilter: false,
          reasoningFilter: false,
          estimatedCost: false,
          tokensPerMinute: false,
        },
        message: "Waiting for Codex activity history.",
      },
    refreshPhase: usageController?.phase ?? "starting",
    lastError: usageController?.lastError ?? null,
    liveDetails: null,
  };
  const claudeProvider: ProviderDesktopState = {
    id: "anthropic-claude",
    name: "Claude",
    snapshot: claudeController?.snapshot ?? null,
    analytics: claudeController?.analytics ?? emptyClaudeAnalytics(),
    refreshPhase: claudeController?.phase ?? "idle",
    lastError: claudeController?.lastError ?? null,
    liveDetails: claudeController?.liveDetails ?? null,
  };
  return {
    settings,
    snapshot: codexProvider.snapshot,
    refreshPhase: codexProvider.refreshPhase,
    lastError: codexProvider.lastError,
    phoneSync:
      phoneSyncServer?.status() ?? {
        enabled: settings.phoneSyncEnabled,
        listening: false,
        port: settings.phoneSyncPort,
        addresses: [],
        pairedDeviceCount: 0,
        pairingCodeActive: false,
        error: null,
      },
    activeProviderId: settings.activeProviderId,
    providers: [codexProvider, claudeProvider],
    claudeIntegration:
      claudeController?.integrationStatus ?? disconnectedClaudeStatus(),
  };
}

function sendState(window: BrowserWindow | null, state: DesktopState): void {
  if (
    window &&
    !window.isDestroyed() &&
    !window.webContents.isDestroyed() &&
    !window.webContents.isLoadingMainFrame()
  ) {
    window.webContents.send(IPC.stateChanged, state);
  }
}

function updateUi(): void {
  const state = currentState();
  notifyUsageThresholds(state);
  if (tray) {
    const codexProvider = state.providers.find(
      (provider) => provider.id === "openai-codex",
    );
    const activeProvider = state.settings.trayProviderDisplay === "both"
      ? codexProvider
      : state.providers.find((provider) => provider.id === state.activeProviderId) ?? state.providers[0];
    const summary = activeProvider?.snapshot
      ? summarizeForTray(activeProvider.snapshot)
      : {
          percentage: null,
          tooltip: `${activeProvider?.name ?? "AI"} Usage — starting`,
          nextResetAt: null,
        };
    tray.setImage(createTrayIcon(
      summary.percentage,
      activeProvider?.id === "anthropic-claude" ? "claude" : "codex",
      trayIconStyle(state.settings),
    ));
    tray.setToolTip(summary.tooltip);
    updateTrayMenu(
      state.settings,
      tray,
      state.settings.trayProviderDisplay === "both" ? "openai-codex" : null,
    );
    if (state.settings.trayProviderDisplay === "both") {
      const claude = state.providers.find((provider) => provider.id === "anthropic-claude");
      if (!claudeTray) claudeTray = createTray("anthropic-claude");
      const claudeSummary = claude?.snapshot ? summarizeForTray(claude.snapshot) : { percentage: null, tooltip: "Claude Usage - start Claude Code" };
      claudeTray.setImage(createTrayIcon(
        claudeSummary.percentage,
        "claude",
        trayIconStyle(state.settings),
      ));
      claudeTray.setToolTip(claudeSummary.tooltip);
      updateTrayMenu(state.settings, claudeTray, "anthropic-claude");
    } else if (claudeTray) {
      claudeTray.destroy();
      claudeTray = null;
    }
  }
  sendState(flyoutWindow, state);
  sendState(widgetWindow, state);
  sendState(dashboardWindow, state);
  sendState(trayIconSettingsWindow, state);
}

function notifyUsageThresholds(state: DesktopState): void {
  if (!state.settings.usageAlertsEnabled || !Notification.isSupported()) return;
  for (const provider of state.providers) {
    for (const windowItem of provider.snapshot?.windows ?? []) {
      const key = `${provider.id}:${windowItem.id}`;
      const remaining = Math.round(windowItem.remainingPercent);
      const previous = alertedThresholds.get(key);
      const resetChanged = previous?.resetAt !== windowItem.resetsAt || (previous !== undefined && remaining > previous.remaining);
      const thresholds = resetChanged ? new Set<number>() : previous?.thresholds ?? new Set<number>();
      if (previous) {
        for (const threshold of state.settings.usageAlertThresholds) {
          if (previous.remaining > threshold && remaining <= threshold && !thresholds.has(threshold)) {
            new Notification({ title: `${provider.name} usage warning`, body: `${windowItem.label} has ${remaining}% remaining${windowItem.resetsAt ? ` until its next reset.` : "."}` }).show();
            thresholds.add(threshold);
          }
        }
      }
      alertedThresholds.set(key, { resetAt: windowItem.resetsAt, remaining, thresholds });
    }
  }
}

function positionFlyout(): void {
  if (!flyoutWindow) return;
  const display = screen.getDisplayNearestPoint(screen.getCursorScreenPoint());
  const { x, y, width, height } = display.workArea;
  flyoutWindow.setPosition(
    Math.round(x + width - FLYOUT_WIDTH - WINDOW_MARGIN),
    Math.round(y + height - FLYOUT_HEIGHT - WINDOW_MARGIN),
    false,
  );
}

function positionWidget(): void {
  if (!widgetWindow) return;
  const { x, y, width, height } = screen.getPrimaryDisplay().workArea;
  widgetWindow.setPosition(
    Math.round(x + width - WIDGET_WIDTH - WINDOW_MARGIN),
    Math.round(y + height - widgetHeight(currentSettings()) - WINDOW_MARGIN),
    false,
  );
}

function widgetHeight(settings: AppSettings): number {
  const baseHeight = settings.widgetProviderDisplay === "both" ? WIDGET_HEIGHT_BOTH : WIDGET_HEIGHT;
  return Math.round(baseHeight * (settings.widgetTextScale / 100));
}

function showFlyout(): void {
  const window = flyoutWindow;
  if (!window || window.isDestroyed()) return;
  positionFlyout();
  const show = () => {
    if (window.isDestroyed()) return;
    window.show();
    window.focus();
  };
  if (window.webContents.isLoadingMainFrame()) {
    window.webContents.once("did-finish-load", show);
  } else {
    show();
  }
}

function showDashboard(): void {
  if (!dashboardWindow || dashboardWindow.isDestroyed()) {
    dashboardWindow = createDashboardWindow();
  }
  const window = dashboardWindow;
  const show = () => {
    if (window.isDestroyed()) return;
    window.show();
    window.focus();
  };
  if (window.webContents.isLoadingMainFrame()) {
    window.webContents.once("did-finish-load", show);
  } else {
    show();
  }
}

function toggleFlyout(): void {
  if (flyoutWindow?.isVisible()) {
    flyoutWindow.hide();
  } else {
    showFlyout();
  }
}

async function showFlyoutForProvider(providerId: AppSettings["activeProviderId"]): Promise<void> {
  if (currentSettings().activeProviderId !== providerId) {
    await applySettingsPatch({ activeProviderId: providerId });
  }
  showFlyout();
}

function rendererUrl(view: "flyout" | "widget" | "dashboard" | "tray-icons"): string | null {
  if (app.isPackaged) return null;
  const developmentServer = process.env.VITE_DEV_SERVER_URL;
  if (!developmentServer) return null;
  let url: URL;
  try {
    url = new URL(developmentServer);
  } catch {
    return null;
  }
  if (
    !["http:", "https:"].includes(url.protocol) ||
    !["127.0.0.1", "localhost", "[::1]"].includes(url.hostname)
  ) {
    return null;
  }
  url.searchParams.set("view", view);
  return url.toString();
}

function isTrustedRendererSource(source: string): boolean {
  try {
    const actual = new URL(source);
    if (actual.protocol === "file:") {
      const expected = new URL(
        pathToFileURL(join(__dirname, "../renderer/index.html")).href,
      );
      actual.search = "";
      actual.hash = "";
      return actual.href === expected.href;
    }
    if (app.isPackaged) {
      return false;
    }
    const configured = rendererUrl("flyout");
    if (!configured) {
      return false;
    }
    const expected = new URL(configured);
    return (
      actual.origin === expected.origin &&
      actual.pathname === expected.pathname
    );
  } catch {
    return false;
  }
}

function assertTrustedIpc(event: IpcMainInvokeEvent): void {
  const source = event.senderFrame?.url ?? event.sender.getURL();
  if (!isTrustedRendererSource(source)) {
    throw new Error("UsageApp rejected IPC from an untrusted renderer.");
  }
}

function secureWindow(window: BrowserWindow): void {
  window.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  window.webContents.on("will-navigate", (event) => {
    event.preventDefault();
  });
}

async function loadRenderer(
  window: BrowserWindow,
  view: "flyout" | "widget" | "dashboard" | "tray-icons",
): Promise<void> {
  const developmentUrl = rendererUrl(view);
  if (developmentUrl) {
    await window.loadURL(developmentUrl);
  } else {
    await window.loadFile(join(__dirname, "../renderer/index.html"), {
      query: { view },
    });
  }
}

function createDashboardWindow(): BrowserWindow {
  const window = new BrowserWindow({
    title: "UsageApp dashboard",
    width: DASHBOARD_WIDTH,
    height: DASHBOARD_HEIGHT,
    minWidth: 900,
    minHeight: 620,
    show: false,
    frame: true,
    resizable: true,
    maximizable: true,
    minimizable: true,
    fullscreenable: true,
    skipTaskbar: false,
    autoHideMenuBar: true,
    backgroundColor: "#0b1018",
    webPreferences: {
      preload: join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      spellcheck: false,
    },
  });
  secureWindow(window);
  window.on("closed", () => {
    if (dashboardWindow === window) {
      dashboardWindow = null;
    }
  });
  window.webContents.on("did-finish-load", () => {
    sendState(window, currentState());
  });
  void loadRenderer(window, "dashboard");
  return window;
}

function createTrayIconSettingsWindow(): BrowserWindow {
  const window = new BrowserWindow({
    title: "UsageApp tray icons",
    width: 640,
    height: 650,
    minWidth: 540,
    minHeight: 560,
    show: false,
    frame: true,
    resizable: true,
    maximizable: false,
    autoHideMenuBar: true,
    backgroundColor: "#0b1018",
    webPreferences: {
      preload: join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      spellcheck: false,
    },
  });
  secureWindow(window);
  window.on("closed", () => {
    if (trayIconSettingsWindow === window) trayIconSettingsWindow = null;
  });
  window.webContents.on("did-finish-load", () => sendState(window, currentState()));
  void loadRenderer(window, "tray-icons");
  return window;
}

function showTrayIconSettings(): void {
  if (!trayIconSettingsWindow || trayIconSettingsWindow.isDestroyed()) {
    trayIconSettingsWindow = createTrayIconSettingsWindow();
  }
  const window = trayIconSettingsWindow;
  const show = () => {
    if (!window.isDestroyed()) {
      window.show();
      window.focus();
    }
  };
  if (window.webContents.isLoadingMainFrame()) window.webContents.once("did-finish-load", show);
  else show();
}

function createFlyoutWindow(): BrowserWindow {
  const window = new BrowserWindow({
    title: "UsageApp",
    width: FLYOUT_WIDTH,
    height: FLYOUT_HEIGHT,
    minWidth: FLYOUT_WIDTH,
    maxWidth: FLYOUT_WIDTH,
    minHeight: FLYOUT_HEIGHT,
    maxHeight: FLYOUT_HEIGHT,
    show: false,
    frame: false,
    resizable: false,
    maximizable: false,
    minimizable: false,
    fullscreenable: false,
    skipTaskbar: true,
    alwaysOnTop: true,
    backgroundColor: "#0b1018",
    webPreferences: {
      preload: join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      spellcheck: false,
    },
  });
  secureWindow(window);
  window.on("blur", () => {
    if (
      !isQuitting &&
      !window.webContents.isDevToolsOpened() &&
      Date.now() >= trayInteractionUntil
    ) {
      window.hide();
    }
  });
  window.on("close", (event) => {
    if (!isQuitting) {
      event.preventDefault();
      window.hide();
    }
  });
  window.webContents.on("did-finish-load", () => {
    sendState(window, currentState());
  });
  void loadRenderer(window, "flyout");
  return window;
}

function createWidgetWindow(): BrowserWindow {
  const height = widgetHeight(currentSettings());
  const window = new BrowserWindow({
    title: "UsageApp compact widget",
    width: WIDGET_WIDTH,
    height,
    minWidth: WIDGET_WIDTH,
    maxWidth: WIDGET_WIDTH,
    minHeight: height,
    maxHeight: height,
    show: false,
    frame: false,
    resizable: false,
    maximizable: false,
    minimizable: false,
    fullscreenable: false,
    skipTaskbar: true,
    alwaysOnTop: true,
    backgroundColor: "#0b1018",
    webPreferences: {
      preload: join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      spellcheck: false,
    },
  });
  secureWindow(window);
  window.webContents.on("did-finish-load", () => {
    sendState(window, currentState());
    positionWidget();
    window.showInactive();
  });
  void loadRenderer(window, "widget");
  return window;
}

function syncWidgetVisibility(settings: AppSettings): void {
  if (settings.showWidget) {
    if (!widgetWindow || widgetWindow.isDestroyed()) {
      widgetWindow = createWidgetWindow();
    } else {
      const height = widgetHeight(settings);
      widgetWindow.setMinimumSize(WIDGET_WIDTH, height);
      widgetWindow.setMaximumSize(WIDGET_WIDTH, height);
      widgetWindow.setSize(WIDGET_WIDTH, height);
      positionWidget();
      widgetWindow.showInactive();
    }
  } else if (widgetWindow) {
    widgetWindow.destroy();
    widgetWindow = null;
  }
}

function applyLoginSetting(settings: AppSettings): void {
  const args = app.isPackaged
    ? ["--hidden"]
    : [app.getAppPath(), "--hidden"];
  app.setLoginItemSettings({
    openAtLogin: settings.launchAtLogin,
    path: process.execPath,
    args,
  });
}

async function refreshProviders(): Promise<void> {
  await Promise.all([
    usageController?.refresh(),
    claudeController?.refresh(),
  ]);
}

function applySettingsPatch(
  patch: Partial<AppSettings>,
): Promise<DesktopState> {
  let result: DesktopState | null = null;
  const operation = settingsApplyQueue.then(async () => {
    if (
      !settingsStore ||
      !usageController ||
      !claudeController ||
      !phoneSyncServer
    ) {
      throw new Error("UsageApp is still starting.");
    }
    const previous = settingsStore.get();
    const settings = await settingsStore.update(patch);
    usageController.updateSettings(settings);
    applyLoginSetting(settings);
    syncWidgetVisibility(settings);
    await Promise.all([
      phoneSyncServer.configure(settings),
      claudeController.configure(settings),
    ]);
    updateUi();

    if (previous.codexCommand !== settings.codexCommand) {
      void usageController.refresh();
    }
    result = currentState();
  });
  settingsApplyQueue = operation.catch(() => {
    // Keep later settings and interface-refresh requests usable.
  });
  return operation.then(() => {
    if (!result) {
      throw new Error("UsageApp settings were not applied.");
    }
    return result;
  });
}

function connectClaude(): Promise<DesktopState> {
  let result: DesktopState | null = null;
  const operation = settingsApplyQueue.then(async () => {
    if (!settingsStore || !claudeController) {
      throw new Error("UsageApp is still starting.");
    }

    await claudeController.connect();
    const status = claudeController.integrationStatus;
    if (
      claudeController.lastError ||
      status.state === "error" ||
      status.state === "disconnected"
    ) {
      await claudeController.configure(settingsStore.get());
      updateUi();
      throw new Error(
        claudeController.lastError ??
          status.message ??
          "Claude monitoring could not connect.",
      );
    }

    let settings: AppSettings;
    try {
      settings = await settingsStore.update({
        claudeEnabled: true,
      });
    } catch (error) {
      await claudeController.disconnect();
      updateUi();
      throw error;
    }
    await claudeController.configure(settings);
    updateUi();
    result = currentState();
  });
  settingsApplyQueue = operation.catch(() => {
    // Keep later settings and provider actions usable.
  });
  return operation.then(() => {
    if (!result) {
      throw new Error("Claude monitoring was not connected.");
    }
    return result;
  });
}

function disconnectClaude(): Promise<DesktopState> {
  let result: DesktopState | null = null;
  const operation = settingsApplyQueue.then(async () => {
    if (!settingsStore || !claudeController) {
      throw new Error("UsageApp is still starting.");
    }

    await claudeController.disconnect();
    let settings: AppSettings;
    try {
      settings = await settingsStore.update({ claudeEnabled: false });
    } catch (error) {
      await claudeController.connect();
      updateUi();
      throw error;
    }
    await claudeController.configure(settings);
    updateUi();
    result = currentState();
  });
  settingsApplyQueue = operation.catch(() => {
    // Keep later settings and provider actions usable.
  });
  return operation.then(() => {
    if (!result) {
      throw new Error("Claude monitoring was not disconnected.");
    }
    return result;
  });
}

function reconfigurePhoneSync(): Promise<void> {
  const operation = settingsApplyQueue.then(async () => {
    if (!phoneSyncServer || !settingsStore) return;
    await phoneSyncServer.configure(settingsStore.get());
    updateUi();
  });
  settingsApplyQueue = operation.catch(() => {
    // A later resume or network check should be allowed to retry.
  });
  return operation;
}

function updateTrayMenu(
  settings: AppSettings,
  target: Tray | null = tray,
  providerId: AppSettings["activeProviderId"] | null = null,
): void {
  if (!target) return;
  const template: MenuItemConstructorOptions[] = [
    {
      label: "Open usage flyout",
      click: () => {
        if (providerId) void showFlyoutForProvider(providerId);
        else showFlyout();
      },
    },
    {
      label: "Refresh now",
      click: () => {
        void refreshProviders();
      },
    },
    {
      label: "Open full dashboard",
      click: () => showDashboard(),
    },
    { type: "separator" },
    {
      label: "Show compact widget",
      type: "checkbox",
      checked: settings.showWidget,
      click: (menuItem) => {
        void applySettingsPatch({ showWidget: menuItem.checked });
      },
    },
    { type: "separator" },
    {
      label: "Quit UsageApp",
      click: () => {
        isQuitting = true;
        app.quit();
      },
    },
  ];
  target.setContextMenu(Menu.buildFromTemplate(template));
}

function createTray(
  providerId: AppSettings["activeProviderId"] | null = null,
): Tray {
  const nextTray = new Tray(createTrayIcon(null));
  nextTray.setToolTip("UsageApp — starting");
  nextTray.on("mouse-down", () => {
    trayInteractionUntil = Date.now() + 250;
  });
  nextTray.on("click", () => {
    const fixedProviderId = currentSettings().trayProviderDisplay === "both"
      ? providerId
      : null;
    if (fixedProviderId) void showFlyoutForProvider(fixedProviderId);
    else toggleFlyout();
  });
  return nextTray;
}

function registerIpc(): void {
  ipcMain.handle(IPC.getState, (event) => {
    assertTrustedIpc(event);
    return currentState();
  });
  ipcMain.handle(IPC.refresh, async (event) => {
    assertTrustedIpc(event);
    await refreshProviders();
    return currentState();
  });
  ipcMain.handle(
    IPC.updateSettings,
    async (event, patch: Partial<AppSettings>) => {
      assertTrustedIpc(event);
      return applySettingsPatch(patch);
    },
  );
  ipcMain.handle(IPC.createPairingCode, (event): PairingCodeInfo => {
    assertTrustedIpc(event);
    if (!phoneSyncServer) {
      throw new Error("Phone sync is still starting.");
    }
    return phoneSyncServer.createPairingCode();
  });
  ipcMain.handle(IPC.revokePhoneTokens, async (event) => {
    assertTrustedIpc(event);
    await phoneSyncServer?.revokeAllTokens();
    updateUi();
    return currentState();
  });
  ipcMain.handle(IPC.connectClaude, async (event) => {
    assertTrustedIpc(event);
    return connectClaude();
  });
  ipcMain.handle(IPC.disconnectClaude, async (event) => {
    assertTrustedIpc(event);
    return disconnectClaude();
  });
  ipcMain.handle(IPC.hideFlyout, (event) => {
    assertTrustedIpc(event);
    flyoutWindow?.hide();
  });
  ipcMain.handle(IPC.showFlyout, (event) => {
    assertTrustedIpc(event);
    showFlyout();
  });
  ipcMain.handle(IPC.showDashboard, (event) => {
    assertTrustedIpc(event);
    showDashboard();
  });
  ipcMain.handle(IPC.showTrayIconSettings, (event) => {
    assertTrustedIpc(event);
    showTrayIconSettings();
  });
  ipcMain.handle(IPC.quit, (event) => {
    assertTrustedIpc(event);
    isQuitting = true;
    app.quit();
  });
}

async function startApplication(): Promise<void> {
  app.setAppUserModelId("com.usageapp.windows");

  settingsStore = new SettingsStore(app.getPath("userData"));
  const settings = await settingsStore.load();
  usageController = new UsageController(settings, updateUi);
  claudeController = new ClaudeController(
    app.getPath("userData"),
    settings,
    updateUi,
  );
  phoneSyncServer = new PhoneSyncServer(
    app.getPath("userData"),
    () => usageController?.snapshot ?? null,
    updateUi,
    settings.phoneSyncPort,
  );

  flyoutWindow = createFlyoutWindow();
  tray = createTray("openai-codex");
  registerIpc();
  applyLoginSetting(settings);
  syncWidgetVisibility(settings);
  await Promise.all([
    phoneSyncServer.configure(settings),
    claudeController.start(),
  ]);
  updateUi();
  usageController.start();

  if (!settings.startMinimized && !launchedHidden) {
    showFlyout();
  }

  powerMonitor.on("resume", () => {
    void reconfigurePhoneSync();
    void refreshProviders();
  });
  phoneSyncBindingTimer = setInterval(() => {
    if (currentSettings().phoneSyncEnabled) {
      void reconfigurePhoneSync();
    }
  }, 60_000);
  phoneSyncBindingTimer.unref();
}

if (!hasSingleInstanceLock) {
  app.quit();
} else {
  app.on("second-instance", () => {
    showFlyout();
  });
  app.on("activate", () => {
    showFlyout();
  });
  app.on("before-quit", () => {
    isQuitting = true;
    if (phoneSyncBindingTimer) {
      clearInterval(phoneSyncBindingTimer);
      phoneSyncBindingTimer = null;
    }
    usageController?.stop();
    void claudeController?.stop();
    void phoneSyncServer?.stop();
  });
  app.on("window-all-closed", () => {
    // A tray app intentionally stays alive with no visible windows.
  });
  void app.whenReady().then(startApplication).catch((error) => {
    console.error("UsageApp could not start:", error);
    isQuitting = true;
    app.quit();
  });
}
