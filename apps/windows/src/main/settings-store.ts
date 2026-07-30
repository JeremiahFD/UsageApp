import type { AppSettings, ProviderId, TrayIconSavedPreset } from "@usageapp/core";
import { DEFAULT_SETTINGS } from "@usageapp/core";
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function booleanSetting(
  value: unknown,
  fallback: boolean,
): boolean {
  return typeof value === "boolean" ? value : fallback;
}

function integerSetting(
  value: unknown,
  fallback: number,
  min: number,
  max: number,
): number {
  if (typeof value !== "number" || !Number.isInteger(value)) {
    return fallback;
  }
  return Math.min(max, Math.max(min, value));
}

function providerSetting(value: unknown): ProviderId {
  return value === "anthropic-claude" ? value : "openai-codex";
}

function textScaleSetting(value: unknown): AppSettings["dashboardTextScale"] {
  return value === 100 || value === 125 || value === 150 || value === 175
    ? value
    : DEFAULT_SETTINGS.dashboardTextScale;
}

function interfaceFontSetting(value: unknown): AppSettings["interfaceFont"] {
  return value === "segoe-ui" ||
    value === "verdana" ||
    value === "tahoma" ||
    value === "arial" ||
    value === "trebuchet-ms" ||
    value === "georgia" ||
    value === "consolas"
    ? value
    : "system";
}

function displayModeSetting(value: unknown): AppSettings["trayProviderDisplay"] {
  return value === "both" ? "both" : "active";
}

function trayIconPresetSetting(value: unknown): AppSettings["trayIconPreset"] {
  return value === "solid-letter" || value === "solid-percent" || value === "badge" || value === "high-contrast" || value === "colored-text" || value === "custom"
    ? value
    : "classic";
}

function trayIconShapeSetting(value: unknown): AppSettings["trayIconShape"] {
  return value === "rounded-square" ? "rounded-square" : "circle";
}

function trayIconContentSetting(value: unknown): AppSettings["trayIconContent"] {
  if (value === "letter") return "provider";
  return value === "percent" || value === "provider" ? value : "auto";
}

function trayIconFillSetting(value: unknown): AppSettings["trayIconFill"] {
  return value === "solid" || value === "transparent" ? value : "dark";
}

function trayIconBorderSetting(value: unknown): AppSettings["trayIconBorder"] {
  return value === "none" || value === "thin" ? value : "thick";
}

function trayIconTextToneSetting(value: unknown): AppSettings["trayIconTextTone"] {
  return value === "light" || value === "dark" || value === "provider" || value === "custom" ? value : "auto";
}

function trayIconFontSetting(value: unknown): AppSettings["trayIconFont"] {
  return value === "bold" || value === "rounded" ? value : "classic";
}

function trayIconColorSetting(value: unknown, fallback: string): string {
  return typeof value === "string" && /^#[0-9a-f]{6}$/i.test(value) ? value.toLowerCase() : fallback;
}

function trayIconSavedPresetsSetting(value: unknown): TrayIconSavedPreset[] {
  if (!Array.isArray(value)) return [];
  const presets: TrayIconSavedPreset[] = [];
  for (const item of value.slice(0, 12)) {
    if (!isRecord(item)) continue;
    const id = typeof item.id === "string" && /^[a-z0-9][a-z0-9_-]{0,63}$/i.test(item.id)
      ? item.id
      : null;
    const name = typeof item.name === "string" ? item.name.trim().slice(0, 40) : "";
    if (!id || !name || presets.some((preset) => preset.id === id)) continue;
    presets.push({
      id,
      name,
      shape: trayIconShapeSetting(item.shape),
      content: trayIconContentSetting(item.content),
      fill: trayIconFillSetting(item.fill),
      border: trayIconBorderSetting(item.border),
      codexColor: trayIconColorSetting(item.codexColor, DEFAULT_SETTINGS.trayIconCodexColor),
      claudeColor: trayIconColorSetting(item.claudeColor, DEFAULT_SETTINGS.trayIconClaudeColor),
      textTone: trayIconTextToneSetting(item.textTone),
      codexTextColor: trayIconColorSetting(item.codexTextColor, DEFAULT_SETTINGS.trayIconCodexTextColor),
      claudeTextColor: trayIconColorSetting(item.claudeTextColor, DEFAULT_SETTINGS.trayIconClaudeTextColor),
      maximizeText: booleanSetting(item.maximizeText, false),
      font: trayIconFontSetting(item.font),
    });
  }
  return presets;
}

function alertThresholds(value: unknown): number[] {
  if (!Array.isArray(value)) return [...DEFAULT_SETTINGS.usageAlertThresholds];
  return [...new Set(value.filter((item): item is number =>
    typeof item === "number" && Number.isInteger(item) && item >= 1 && item <= 99,
  ))].sort((left, right) => right - left).slice(0, 8);
}

export function sanitizeSettings(value: unknown): AppSettings {
  const source = isRecord(value) ? value : {};
  const savedPresets = trayIconSavedPresetsSetting(source.trayIconSavedPresets);
  const codexCommand =
    typeof source.codexCommand === "string" &&
    source.codexCommand.trim().length > 0
      ? source.codexCommand.trim()
      : DEFAULT_SETTINGS.codexCommand;

  return {
    launchAtLogin: booleanSetting(
      source.launchAtLogin,
      DEFAULT_SETTINGS.launchAtLogin,
    ),
    showWidget: booleanSetting(
      source.showWidget,
      DEFAULT_SETTINGS.showWidget,
    ),
    startMinimized: booleanSetting(
      source.startMinimized,
      DEFAULT_SETTINGS.startMinimized,
    ),
    refreshIntervalMinutes: integerSetting(
      source.refreshIntervalMinutes,
      DEFAULT_SETTINGS.refreshIntervalMinutes,
      1,
      60,
    ),
    phoneSyncEnabled: booleanSetting(
      source.phoneSyncEnabled,
      DEFAULT_SETTINGS.phoneSyncEnabled,
    ),
    phoneSyncPort: integerSetting(
      source.phoneSyncPort,
      DEFAULT_SETTINGS.phoneSyncPort,
      1_024,
      65_535,
    ),
    codexCommand,
    activeProviderId: providerSetting(source.activeProviderId),
    claudeEnabled: booleanSetting(
      source.claudeEnabled,
      DEFAULT_SETTINGS.claudeEnabled,
    ),
    claudeTelemetryPort: integerSetting(
      source.claudeTelemetryPort,
      DEFAULT_SETTINGS.claudeTelemetryPort,
      1_024,
      65_535,
    ),
    dashboardTextScale: textScaleSetting(source.dashboardTextScale),
    flyoutTextScale: textScaleSetting(source.flyoutTextScale),
    widgetTextScale: textScaleSetting(source.widgetTextScale),
    interfaceFont: interfaceFontSetting(source.interfaceFont),
    trayProviderDisplay: displayModeSetting(source.trayProviderDisplay),
    trayIconPreset: trayIconPresetSetting(source.trayIconPreset),
    trayIconShape: trayIconShapeSetting(source.trayIconShape),
    trayIconContent: trayIconContentSetting(source.trayIconContent),
    trayIconFill: trayIconFillSetting(source.trayIconFill),
    trayIconBorder: trayIconBorderSetting(source.trayIconBorder),
    trayIconCodexColor: trayIconColorSetting(source.trayIconCodexColor, DEFAULT_SETTINGS.trayIconCodexColor),
    trayIconClaudeColor: trayIconColorSetting(source.trayIconClaudeColor, DEFAULT_SETTINGS.trayIconClaudeColor),
    trayIconTextTone: trayIconTextToneSetting(source.trayIconTextTone),
    trayIconCodexTextColor: trayIconColorSetting(source.trayIconCodexTextColor, DEFAULT_SETTINGS.trayIconCodexTextColor),
    trayIconClaudeTextColor: trayIconColorSetting(source.trayIconClaudeTextColor, DEFAULT_SETTINGS.trayIconClaudeTextColor),
    trayIconMaximizeText: booleanSetting(source.trayIconMaximizeText, DEFAULT_SETTINGS.trayIconMaximizeText),
    trayIconFont: trayIconFontSetting(source.trayIconFont),
    trayIconSavedPresets: savedPresets,
    trayIconActiveSavedPresetId: typeof source.trayIconActiveSavedPresetId === "string" &&
      savedPresets.some((preset) => preset.id === source.trayIconActiveSavedPresetId)
      ? source.trayIconActiveSavedPresetId
      : null,
    widgetProviderDisplay: displayModeSetting(source.widgetProviderDisplay),
    usageAlertsEnabled: booleanSetting(source.usageAlertsEnabled, DEFAULT_SETTINGS.usageAlertsEnabled),
    usageAlertThresholds: alertThresholds(source.usageAlertThresholds),
  };
}

export function sanitizeSettingsPatch(
  value: unknown,
): Partial<AppSettings> {
  if (!isRecord(value)) {
    return {};
  }

  const patch: Partial<AppSettings> = {};
  if (typeof value.launchAtLogin === "boolean") {
    patch.launchAtLogin = value.launchAtLogin;
  }
  if (typeof value.showWidget === "boolean") {
    patch.showWidget = value.showWidget;
  }
  if (typeof value.startMinimized === "boolean") {
    patch.startMinimized = value.startMinimized;
  }
  if (
    typeof value.refreshIntervalMinutes === "number" &&
    Number.isInteger(value.refreshIntervalMinutes)
  ) {
    patch.refreshIntervalMinutes = Math.min(
      60,
      Math.max(1, value.refreshIntervalMinutes),
    );
  }
  if (typeof value.phoneSyncEnabled === "boolean") {
    patch.phoneSyncEnabled = value.phoneSyncEnabled;
  }
  if (
    typeof value.phoneSyncPort === "number" &&
    Number.isInteger(value.phoneSyncPort)
  ) {
    patch.phoneSyncPort = Math.min(
      65_535,
      Math.max(1_024, value.phoneSyncPort),
    );
  }
  if (
    typeof value.codexCommand === "string" &&
    value.codexCommand.trim().length > 0
  ) {
    patch.codexCommand = value.codexCommand.trim();
  }
  if (
    value.activeProviderId === "openai-codex" ||
    value.activeProviderId === "anthropic-claude"
  ) {
    patch.activeProviderId = value.activeProviderId;
  }
  if (typeof value.claudeEnabled === "boolean") {
    patch.claudeEnabled = value.claudeEnabled;
  }
  if (
    typeof value.claudeTelemetryPort === "number" &&
    Number.isInteger(value.claudeTelemetryPort)
  ) {
    patch.claudeTelemetryPort = Math.min(
      65_535,
      Math.max(1_024, value.claudeTelemetryPort),
    );
  }
  if (
    value.dashboardTextScale === 100 ||
    value.dashboardTextScale === 125 ||
    value.dashboardTextScale === 150 ||
    value.dashboardTextScale === 175
  ) {
    patch.dashboardTextScale = value.dashboardTextScale;
  }
  if (
    value.flyoutTextScale === 100 ||
    value.flyoutTextScale === 125 ||
    value.flyoutTextScale === 150 ||
    value.flyoutTextScale === 175
  ) {
    patch.flyoutTextScale = value.flyoutTextScale;
  }
  if (
    value.widgetTextScale === 100 ||
    value.widgetTextScale === 125 ||
    value.widgetTextScale === 150 ||
    value.widgetTextScale === 175
  ) {
    patch.widgetTextScale = value.widgetTextScale;
  }
  if (
    value.interfaceFont === "system" ||
    value.interfaceFont === "segoe-ui" ||
    value.interfaceFont === "verdana" ||
    value.interfaceFont === "tahoma" ||
    value.interfaceFont === "arial" ||
    value.interfaceFont === "trebuchet-ms" ||
    value.interfaceFont === "georgia" ||
    value.interfaceFont === "consolas"
  ) {
    patch.interfaceFont = value.interfaceFont;
  }
  if (value.trayProviderDisplay === "active" || value.trayProviderDisplay === "both") {
    patch.trayProviderDisplay = value.trayProviderDisplay;
  }
  if (["classic", "solid-letter", "solid-percent", "badge", "high-contrast", "colored-text", "custom"].includes(value.trayIconPreset as string)) {
    patch.trayIconPreset = value.trayIconPreset as AppSettings["trayIconPreset"];
  }
  if (value.trayIconShape === "circle" || value.trayIconShape === "rounded-square") {
    patch.trayIconShape = value.trayIconShape;
  }
  if (value.trayIconContent === "auto" || value.trayIconContent === "percent" || value.trayIconContent === "provider") {
    patch.trayIconContent = value.trayIconContent;
  }
  if (value.trayIconFill === "solid" || value.trayIconFill === "dark" || value.trayIconFill === "transparent") patch.trayIconFill = value.trayIconFill;
  if (value.trayIconBorder === "none" || value.trayIconBorder === "thin" || value.trayIconBorder === "thick") patch.trayIconBorder = value.trayIconBorder;
  if (typeof value.trayIconCodexColor === "string" && /^#[0-9a-f]{6}$/i.test(value.trayIconCodexColor)) patch.trayIconCodexColor = value.trayIconCodexColor.toLowerCase();
  if (typeof value.trayIconClaudeColor === "string" && /^#[0-9a-f]{6}$/i.test(value.trayIconClaudeColor)) patch.trayIconClaudeColor = value.trayIconClaudeColor.toLowerCase();
  if (value.trayIconTextTone === "auto" || value.trayIconTextTone === "light" || value.trayIconTextTone === "dark" || value.trayIconTextTone === "provider" || value.trayIconTextTone === "custom") patch.trayIconTextTone = value.trayIconTextTone;
  if (typeof value.trayIconCodexTextColor === "string" && /^#[0-9a-f]{6}$/i.test(value.trayIconCodexTextColor)) patch.trayIconCodexTextColor = value.trayIconCodexTextColor.toLowerCase();
  if (typeof value.trayIconClaudeTextColor === "string" && /^#[0-9a-f]{6}$/i.test(value.trayIconClaudeTextColor)) patch.trayIconClaudeTextColor = value.trayIconClaudeTextColor.toLowerCase();
  if (typeof value.trayIconMaximizeText === "boolean") patch.trayIconMaximizeText = value.trayIconMaximizeText;
  if (value.trayIconFont === "classic" || value.trayIconFont === "bold" || value.trayIconFont === "rounded") patch.trayIconFont = value.trayIconFont;
  if (Array.isArray(value.trayIconSavedPresets)) patch.trayIconSavedPresets = trayIconSavedPresetsSetting(value.trayIconSavedPresets);
  if (value.trayIconActiveSavedPresetId === null || typeof value.trayIconActiveSavedPresetId === "string") {
    patch.trayIconActiveSavedPresetId = value.trayIconActiveSavedPresetId;
  }
  if (value.widgetProviderDisplay === "active" || value.widgetProviderDisplay === "both") {
    patch.widgetProviderDisplay = value.widgetProviderDisplay;
  }
  if (typeof value.usageAlertsEnabled === "boolean") {
    patch.usageAlertsEnabled = value.usageAlertsEnabled;
  }
  if (Array.isArray(value.usageAlertThresholds)) {
    patch.usageAlertThresholds = alertThresholds(value.usageAlertThresholds);
  }
  return patch;
}

export class SettingsStore {
  private readonly path: string;
  private settings: AppSettings = { ...DEFAULT_SETTINGS };
  private updateQueue: Promise<void> = Promise.resolve();

  constructor(userDataPath: string) {
    this.path = join(userDataPath, "settings.json");
  }

  async load(): Promise<AppSettings> {
    try {
      const raw = await readFile(this.path, "utf8");
      this.settings = sanitizeSettings(JSON.parse(raw) as unknown);
    } catch (error) {
      const code = (error as NodeJS.ErrnoException).code;
      if (code !== "ENOENT") {
        console.warn("Could not load UsageApp settings:", error);
      }
      this.settings = { ...DEFAULT_SETTINGS };
    }
    return this.get();
  }

  get(): AppSettings {
    return {
      ...this.settings,
      usageAlertThresholds: [...this.settings.usageAlertThresholds],
      trayIconSavedPresets: this.settings.trayIconSavedPresets.map((preset) => ({ ...preset })),
    };
  }

  async update(patchValue: unknown): Promise<AppSettings> {
    const patch = sanitizeSettingsPatch(patchValue);
    let result: AppSettings | null = null;
    const operation = this.updateQueue.then(async () => {
      const candidate = sanitizeSettings({ ...this.settings, ...patch });
      await this.persist(candidate);
      this.settings = candidate;
      result = this.get();
    });
    this.updateQueue = operation.catch(() => {
      // Keep the queue usable after a failed disk write. The caller still
      // receives the original rejection.
    });
    await operation;
    if (!result) {
      throw new Error("UsageApp settings were not updated.");
    }
    return result;
  }

  private async persist(settings: AppSettings): Promise<void> {
    await mkdir(dirname(this.path), { recursive: true });
    const temporaryPath = `${this.path}.tmp`;
    await writeFile(
      temporaryPath,
      `${JSON.stringify(settings, null, 2)}\n`,
      "utf8",
    );
    await rename(temporaryPath, this.path);
  }
}
