import type { AppSettings } from "@usageapp/core";
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

export function sanitizeSettings(value: unknown): AppSettings {
  const source = isRecord(value) ? value : {};
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
    return { ...this.settings };
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
