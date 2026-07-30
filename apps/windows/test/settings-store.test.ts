import { describe, expect, it } from "vitest";

import {
  sanitizeSettings,
  sanitizeSettingsPatch,
} from "../src/main/settings-store";

describe("taskbar icon font settings", () => {
  it("keeps the taskbar font independent from the interface font", () => {
    const settings = sanitizeSettings({
      interfaceFont: "georgia",
      trayIconFont: "consolas",
    });

    expect(settings.interfaceFont).toBe("georgia");
    expect(settings.trayIconFont).toBe("consolas");
  });

  it("accepts Windows taskbar fonts in partial settings updates", () => {
    expect(sanitizeSettingsPatch({ trayIconFont: "segoe-ui" })).toEqual({
      trayIconFont: "segoe-ui",
    });
    expect(sanitizeSettingsPatch({ trayIconFont: "verdana" })).toEqual({
      trayIconFont: "verdana",
    });
  });

  it("falls back safely when a saved taskbar font is unknown", () => {
    expect(sanitizeSettings({ trayIconFont: "missing-font" }).trayIconFont)
      .toBe("classic");
    expect(sanitizeSettingsPatch({ trayIconFont: "missing-font" })).toEqual(
      {},
    );
  });
});
