import { describe, expect, it } from "vitest";
import { DEFAULT_SETTINGS } from "@usageapp/core";

import { savedTrayPresetPatch } from "../src/main/tray-preset";

describe("discarding taskbar preset edits", () => {
  it("restores every persisted icon field from the active saved preset", () => {
    const patch = savedTrayPresetPatch({
      ...DEFAULT_SETTINGS,
      trayIconPreset: "custom",
      trayIconShape: "circle",
      trayIconFont: "georgia",
      trayIconActiveSavedPresetId: "saved-1",
      trayIconSavedPresets: [{
        id: "saved-1",
        name: "Readable",
        shape: "rounded-square",
        content: "percent",
        fill: "solid",
        border: "none",
        codexColor: "#0072b2",
        claudeColor: "#e69f00",
        textTone: "dark",
        codexTextColor: "#07121f",
        claudeTextColor: "#1d0d05",
        maximizeText: true,
        font: "segoe-ui",
      }],
    });

    expect(patch).toMatchObject({
      trayIconPreset: "custom",
      trayIconShape: "rounded-square",
      trayIconFill: "solid",
      trayIconBorder: "none",
      trayIconFont: "segoe-ui",
      trayIconMaximizeText: true,
    });
  });

  it("does nothing when no saved preset is active", () => {
    expect(savedTrayPresetPatch(DEFAULT_SETTINGS)).toBeNull();
  });
});
