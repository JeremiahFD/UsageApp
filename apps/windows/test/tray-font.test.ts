import { describe, expect, it } from "vitest";

import { drawSystemTrayFontText } from "../src/main/tray-font";

describe("Windows taskbar font rendering", () => {
  it("leaves legacy pixel fonts for the bitmap renderer", () => {
    const pixels = new Uint8Array(64 * 64 * 4);
    expect(
      drawSystemTrayFontText(
        pixels,
        64,
        64,
        "90",
        [255, 255, 255, 255],
        "bold",
        true,
      ),
    ).toBe(false);
  });

  it.runIf(process.platform === "win32")(
    "rasterizes an installed Windows font into the tray buffer",
    () => {
      const pixels = new Uint8Array(64 * 64 * 4);
      expect(
        drawSystemTrayFontText(
          pixels,
          64,
          64,
          "90",
          [56, 189, 248, 255],
          "segoe-ui",
          true,
        ),
      ).toBe(true);
      expect(
        pixels.some((value, index) => index % 4 === 3 && value > 0),
      ).toBe(true);
    },
  );
});
