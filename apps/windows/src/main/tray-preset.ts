import type { AppSettings } from "@usageapp/core";

/**
 * Restores the currently selected saved preset after a user discards live
 * customizer edits. Preset names are edited only in renderer state, so the
 * persisted icon fields are the only values that need to be restored.
 */
export function savedTrayPresetPatch(
  settings: AppSettings,
): Partial<AppSettings> | null {
  const activeId = settings.trayIconActiveSavedPresetId;
  if (!activeId) return null;
  const preset = settings.trayIconSavedPresets.find(
    (item) => item.id === activeId,
  );
  if (!preset) return null;

  return {
    trayIconPreset: "custom",
    trayIconShape: preset.shape,
    trayIconContent: preset.content,
    trayIconFill: preset.fill,
    trayIconBorder: preset.border,
    trayIconCodexColor: preset.codexColor,
    trayIconClaudeColor: preset.claudeColor,
    trayIconTextTone: preset.textTone,
    trayIconCodexTextColor: preset.codexTextColor,
    trayIconClaudeTextColor: preset.claudeTextColor,
    trayIconMaximizeText: preset.maximizeText,
    trayIconFont: preset.font,
  };
}
