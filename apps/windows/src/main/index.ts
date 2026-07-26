import {
  app,
  BrowserWindow,
  ipcMain,
  Menu,
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
  type DesktopState,
  type PairingCodeInfo,
} from "../shared/desktop";
import { PhoneSyncServer } from "./phone-sync-server";
import { SettingsStore } from "./settings-store";
import { createTrayIcon } from "./tray-icon";
import { UsageController } from "./usage-controller";

const FLYOUT_WIDTH = 410;
const FLYOUT_HEIGHT = 650;
const WIDGET_WIDTH = 320;
const WIDGET_HEIGHT = 146;
const WINDOW_MARGIN = 12;

let tray: Tray | null = null;
let flyoutWindow: BrowserWindow | null = null;
let widgetWindow: BrowserWindow | null = null;
let settingsStore: SettingsStore | null = null;
let usageController: UsageController | null = null;
let phoneSyncServer: PhoneSyncServer | null = null;
let isQuitting = false;
let trayInteractionUntil = 0;
let phoneSyncBindingTimer: NodeJS.Timeout | null = null;
let settingsApplyQueue: Promise<void> = Promise.resolve();

const launchedHidden = process.argv.includes("--hidden");
const hasSingleInstanceLock = app.requestSingleInstanceLock();

function currentSettings(): AppSettings {
  return settingsStore?.get() ?? { ...DEFAULT_SETTINGS };
}

function currentState(): DesktopState {
  const settings = currentSettings();
  return {
    settings,
    snapshot: usageController?.snapshot ?? null,
    refreshPhase: usageController?.phase ?? "starting",
    lastError: usageController?.lastError ?? null,
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
  if (tray) {
    const summary = state.snapshot
      ? summarizeForTray(state.snapshot)
      : {
          percentage: null,
          tooltip: "Codex Usage — starting",
          nextResetAt: null,
        };
    tray.setImage(createTrayIcon(summary.percentage));
    tray.setToolTip(summary.tooltip);
    updateTrayMenu(state.settings);
  }
  sendState(flyoutWindow, state);
  sendState(widgetWindow, state);
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
    Math.round(y + height - WIDGET_HEIGHT - WINDOW_MARGIN),
    false,
  );
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

function toggleFlyout(): void {
  if (flyoutWindow?.isVisible()) {
    flyoutWindow.hide();
  } else {
    showFlyout();
  }
}

function rendererUrl(view: "flyout" | "widget"): string | null {
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
  view: "flyout" | "widget",
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
  const window = new BrowserWindow({
    title: "UsageApp compact widget",
    width: WIDGET_WIDTH,
    height: WIDGET_HEIGHT,
    minWidth: WIDGET_WIDTH,
    maxWidth: WIDGET_WIDTH,
    minHeight: WIDGET_HEIGHT,
    maxHeight: WIDGET_HEIGHT,
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

function applySettingsPatch(
  patch: Partial<AppSettings>,
): Promise<DesktopState> {
  let result: DesktopState | null = null;
  const operation = settingsApplyQueue.then(async () => {
    if (!settingsStore || !usageController || !phoneSyncServer) {
      throw new Error("UsageApp is still starting.");
    }
    const previous = settingsStore.get();
    const settings = await settingsStore.update(patch);
    usageController.updateSettings(settings);
    applyLoginSetting(settings);
    syncWidgetVisibility(settings);
    await phoneSyncServer.configure(settings);
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

function updateTrayMenu(settings: AppSettings): void {
  if (!tray) return;
  const template: MenuItemConstructorOptions[] = [
    {
      label: "Open Codex usage",
      click: () => showFlyout(),
    },
    {
      label: "Refresh now",
      click: () => {
        void usageController?.refresh();
      },
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
  tray.setContextMenu(Menu.buildFromTemplate(template));
}

function createTray(): Tray {
  const nextTray = new Tray(createTrayIcon(null));
  nextTray.setToolTip("Codex Usage — starting");
  nextTray.on("mouse-down", () => {
    trayInteractionUntil = Date.now() + 250;
  });
  nextTray.on("click", () => toggleFlyout());
  return nextTray;
}

function registerIpc(): void {
  ipcMain.handle(IPC.getState, (event) => {
    assertTrustedIpc(event);
    return currentState();
  });
  ipcMain.handle(IPC.refresh, async (event) => {
    assertTrustedIpc(event);
    await usageController?.refresh();
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
  ipcMain.handle(IPC.hideFlyout, (event) => {
    assertTrustedIpc(event);
    flyoutWindow?.hide();
  });
  ipcMain.handle(IPC.showFlyout, (event) => {
    assertTrustedIpc(event);
    showFlyout();
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
  phoneSyncServer = new PhoneSyncServer(
    app.getPath("userData"),
    () => usageController?.snapshot ?? null,
    updateUi,
    settings.phoneSyncPort,
  );

  flyoutWindow = createFlyoutWindow();
  tray = createTray();
  registerIpc();
  applyLoginSetting(settings);
  syncWidgetVisibility(settings);
  await phoneSyncServer.configure(settings);
  updateUi();
  usageController.start();

  if (!settings.startMinimized && !launchedHidden) {
    showFlyout();
  }

  powerMonitor.on("resume", () => {
    void reconfigurePhoneSync();
    void usageController?.refresh();
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
