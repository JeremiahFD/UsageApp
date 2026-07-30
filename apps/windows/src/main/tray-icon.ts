import { nativeImage, type NativeImage } from "electron";
import type { TrayIconFont } from "@usageapp/core";
import { deflateSync } from "node:zlib";
import { drawSystemTrayFontText } from "./tray-font";

type Color = readonly [number, number, number, number];

const WIDTH = 64;
const HEIGHT = 64;
const TRANSPARENT: Color = [0, 0, 0, 0];
const BACKGROUND: Color = [17, 24, 39, 255];
const TRACK: Color = [68, 79, 96, 255];
const TEXT: Color = [248, 250, 252, 255];
const DARK_TEXT: Color = [10, 20, 32, 255];

export interface TrayIconStyle {
  preset: "classic" | "solid-letter" | "solid-percent" | "badge" | "high-contrast" | "colored-text" | "custom";
  shape: "circle" | "rounded-square";
  content: "percent" | "provider" | "auto";
  fill: "solid" | "dark" | "transparent";
  border: "none" | "thin" | "thick";
  codexColor: string;
  claudeColor: string;
  textTone: "auto" | "light" | "dark" | "provider" | "custom";
  codexTextColor: string;
  claudeTextColor: string;
  maximizeText: boolean;
  font: TrayIconFont;
}

function hexColor(value: string, fallback: Color): Color {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(value);
  return match
    ? [Number.parseInt(match[1] ?? "0", 16), Number.parseInt(match[2] ?? "0", 16), Number.parseInt(match[3] ?? "0", 16), 255]
    : fallback;
}

function contrastText(background: Color): Color {
  const luminance = (0.2126 * background[0] + 0.7152 * background[1] + 0.0722 * background[2]) / 255;
  return luminance > 0.56 ? DARK_TEXT : TEXT;
}

interface BitmapFont {
  width: number;
  height: number;
  /** Glyph cell size in pixels, chosen so the label still fits the icon. */
  scale(length: number, maximize: boolean): number;
  glyphs: Record<string, readonly string[]>;
}

const CLASSIC_GLYPHS: Record<string, readonly string[]> = {
  "0": ["111", "101", "101", "101", "111"],
  "1": ["010", "110", "010", "010", "111"],
  "2": ["111", "001", "111", "100", "111"],
  "3": ["111", "001", "111", "001", "111"],
  "4": ["101", "101", "111", "001", "001"],
  "5": ["111", "100", "111", "001", "111"],
  "6": ["111", "100", "111", "101", "111"],
  "7": ["111", "001", "010", "010", "010"],
  "8": ["111", "101", "111", "101", "111"],
  "9": ["111", "101", "111", "001", "111"],
  "-": ["000", "000", "111", "000", "000"],
  "?": ["111", "001", "011", "000", "010"],
  "C": ["111", "100", "100", "100", "111"],
  "A": ["010", "101", "111", "101", "101"],
  "O": ["111", "101", "101", "101", "111"],
};

/** Two-pixel strokes, for the most legible number at small tray sizes. */
const BOLD_GLYPHS: Record<string, readonly string[]> = {
  "0": ["01110", "11011", "11011", "11011", "11011", "11011", "01110"],
  "1": ["00110", "01110", "00110", "00110", "00110", "00110", "01111"],
  "2": ["01110", "11011", "00011", "00110", "01100", "11000", "11111"],
  "3": ["11111", "00110", "01110", "00011", "00011", "11011", "01110"],
  "4": ["00011", "00111", "01111", "11011", "11111", "00011", "00011"],
  "5": ["11111", "11000", "11110", "00011", "00011", "11011", "01110"],
  "6": ["01110", "11000", "11000", "11110", "11011", "11011", "01110"],
  "7": ["11111", "00011", "00110", "00110", "01100", "01100", "01100"],
  "8": ["01110", "11011", "11011", "01110", "11011", "11011", "01110"],
  "9": ["01110", "11011", "11011", "01111", "00011", "00011", "01110"],
  "-": ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
  "?": ["01110", "11011", "00011", "00110", "00100", "00000", "00100"],
  "C": ["01110", "11011", "11000", "11000", "11000", "11011", "01110"],
  "A": ["01110", "11011", "11011", "11111", "11011", "11011", "11011"],
  "O": ["01110", "11011", "11011", "11011", "11011", "11011", "01110"],
};

/** Single-pixel strokes with clipped corners, for a lighter look. */
const ROUNDED_GLYPHS: Record<string, readonly string[]> = {
  "0": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
  "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
  "2": ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
  "3": ["01110", "10001", "00001", "00110", "00001", "10001", "01110"],
  "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
  "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
  "6": ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
  "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
  "8": ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
  "9": ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
  "-": ["00000", "00000", "00000", "01110", "00000", "00000", "00000"],
  "?": ["01110", "10001", "00001", "00010", "00100", "00000", "00100"],
  "C": ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
  "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
  "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
};

/** Both tall faces are 5x7, so they share one fitting table. */
function tallScale(length: number, maximize: boolean): number {
  if (maximize) return length >= 3 ? 3 : length === 2 ? 5 : 7;
  return length >= 3 ? 3 : length === 2 ? 4 : 5;
}

const FONTS: Partial<Record<TrayIconStyle["font"], BitmapFont>> = {
  classic: {
    width: 3,
    height: 5,
    // Preserved exactly so existing icons keep the size users chose them at.
    scale: (length, maximize) =>
      maximize
        ? length >= 3 ? 5 : length === 2 ? 7 : 9
        : length >= 3 ? 4 : 5,
    glyphs: CLASSIC_GLYPHS,
  },
  bold: { width: 5, height: 7, scale: tallScale, glyphs: BOLD_GLYPHS },
  rounded: { width: 5, height: 7, scale: tallScale, glyphs: ROUNDED_GLYPHS },
};

function setPixel(
  pixels: Uint8Array,
  x: number,
  y: number,
  color: Color,
): void {
  if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT) {
    return;
  }
  const index = (y * WIDTH + x) * 4;
  pixels[index] = color[0];
  pixels[index + 1] = color[1];
  pixels[index + 2] = color[2];
  pixels[index + 3] = color[3];
}

function fillCircle(
  pixels: Uint8Array,
  centerX: number,
  centerY: number,
  radius: number,
  color: Color,
): void {
  const radiusSquared = radius * radius;
  for (let y = centerY - radius; y <= centerY + radius; y += 1) {
    for (let x = centerX - radius; x <= centerX + radius; x += 1) {
      const dx = x - centerX;
      const dy = y - centerY;
      if (dx * dx + dy * dy <= radiusSquared) {
        setPixel(pixels, x, y, color);
      }
    }
  }
}

function fillRoundedSquare(
  pixels: Uint8Array,
  left: number,
  top: number,
  size: number,
  radius: number,
  color: Color,
): void {
  const right = left + size - 1;
  const bottom = top + size - 1;
  for (let y = top; y <= bottom; y += 1) {
    for (let x = left; x <= right; x += 1) {
      const cornerX = x < left + radius ? left + radius : x > right - radius ? right - radius : x;
      const cornerY = y < top + radius ? top + radius : y > bottom - radius ? bottom - radius : y;
      const dx = x - cornerX;
      const dy = y - cornerY;
      if (dx * dx + dy * dy <= radius * radius) setPixel(pixels, x, y, color);
    }
  }
}

function fillShape(
  pixels: Uint8Array,
  shape: TrayIconStyle["shape"],
  color: Color,
  inset = 5,
): void {
  if (shape === "rounded-square") {
    fillRoundedSquare(pixels, inset, inset, 64 - inset * 2, Math.max(4, 14 - inset), color);
  } else {
    fillCircle(pixels, 32, 32, Math.max(1, 32 - inset), color);
  }
}

function drawShapeBorder(
  pixels: Uint8Array,
  shape: TrayIconStyle["shape"],
  color: Color,
  inner: Color,
  thickness: number,
): void {
  fillShape(pixels, shape, color, 3);
  fillShape(pixels, shape, inner, 3 + thickness);
}

function drawRing(
  pixels: Uint8Array,
  percentage: number | null,
  active: Color,
): void {
  const center = 31.5;
  const innerSquared = 26 * 26;
  const outerSquared = 31 * 31;
  const fraction =
    percentage === null ? 0 : Math.min(100, Math.max(0, percentage)) / 100;

  for (let y = 0; y < HEIGHT; y += 1) {
    for (let x = 0; x < WIDTH; x += 1) {
      const dx = x - center;
      const dy = y - center;
      const distanceSquared = dx * dx + dy * dy;
      if (
        distanceSquared < innerSquared ||
        distanceSquared > outerSquared
      ) {
        continue;
      }

      let angle = Math.atan2(dy, dx) + Math.PI / 2;
      if (angle < 0) angle += Math.PI * 2;
      const color =
        percentage !== null && angle <= fraction * Math.PI * 2
          ? active
          : TRACK;
      setPixel(pixels, x, y, color);
    }
  }
}

function drawText(
  pixels: Uint8Array,
  value: string,
  color: Color,
  font: BitmapFont,
  maximize = false,
): void {
  const scale = font.scale(value.length, maximize);
  // The classic face is narrow enough to want a full cell between glyphs; the
  // 5-wide faces would overflow the icon at that spacing.
  const gap =
    font.width >= 5
      ? Math.max(1, Math.floor(scale / 2))
      : maximize
        ? Math.max(2, Math.floor(scale / 2))
        : scale;
  const glyphWidth = font.width * scale;
  const totalWidth = value.length * glyphWidth + (value.length - 1) * gap;
  const startX = Math.round((WIDTH - totalWidth) / 2);
  const startY = Math.round((HEIGHT - font.height * scale) / 2);

  [...value].forEach((character, characterIndex) => {
    const glyph = font.glyphs[character] ?? font.glyphs["?"];
    if (!glyph) return;
    glyph.forEach((row, rowIndex) => {
      [...row].forEach((pixel, columnIndex) => {
        if (pixel !== "1") return;
        const originX =
          startX + characterIndex * (glyphWidth + gap) + columnIndex * scale;
        const originY = startY + rowIndex * scale;
        for (let y = 0; y < scale; y += 1) {
          for (let x = 0; x < scale; x += 1) {
            setPixel(pixels, originX + x, originY + y, color);
          }
        }
      });
    });
  });
}

let crcTable: Uint32Array | null = null;

function getCrcTable(): Uint32Array {
  if (crcTable) return crcTable;
  const table = new Uint32Array(256);
  for (let index = 0; index < 256; index += 1) {
    let value = index;
    for (let bit = 0; bit < 8; bit += 1) {
      value =
        (value & 1) !== 0
          ? 0xedb88320 ^ (value >>> 1)
          : value >>> 1;
    }
    table[index] = value >>> 0;
  }
  crcTable = table;
  return table;
}

function crc32(buffer: Buffer): number {
  const table = getCrcTable();
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc = (table[(crc ^ byte) & 0xff] ?? 0) ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function pngChunk(type: string, data: Buffer): Buffer {
  const typeBuffer = Buffer.from(type, "ascii");
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const checksum = Buffer.alloc(4);
  checksum.writeUInt32BE(crc32(Buffer.concat([typeBuffer, data])));
  return Buffer.concat([length, typeBuffer, data, checksum]);
}

function encodePng(pixels: Uint8Array): Buffer {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(WIDTH, 0);
  header.writeUInt32BE(HEIGHT, 4);
  header[8] = 8;
  header[9] = 6;
  header[10] = 0;
  header[11] = 0;
  header[12] = 0;

  const rowSize = WIDTH * 4;
  const scanlines = Buffer.alloc((rowSize + 1) * HEIGHT);
  for (let y = 0; y < HEIGHT; y += 1) {
    const outputOffset = y * (rowSize + 1);
    scanlines[outputOffset] = 0;
    const sourceOffset = y * rowSize;
    Buffer.from(pixels.buffer, pixels.byteOffset + sourceOffset, rowSize).copy(
      scanlines,
      outputOffset + 1,
    );
  }

  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    pngChunk("IHDR", header),
    pngChunk("IDAT", deflateSync(scanlines, { level: 9 })),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}

export function createTrayIcon(
  percentage: number | null,
  provider: "codex" | "claude" = "codex",
  style: TrayIconStyle = {
    preset: "classic",
    shape: "circle",
    content: "auto",
    fill: "dark",
    border: "thick",
    codexColor: "#38bdf8",
    claudeColor: "#e89a62",
    textTone: "auto",
    codexTextColor: "#38bdf8",
    claudeTextColor: "#e89a62",
    maximizeText: false,
    font: "classic",
  },
): NativeImage {
  const pixels = new Uint8Array(WIDTH * HEIGHT * 4);
  for (let index = 0; index < pixels.length; index += 4) {
    pixels[index] = TRANSPARENT[0];
    pixels[index + 1] = TRANSPARENT[1];
    pixels[index + 2] = TRANSPARENT[2];
    pixels[index + 3] = TRANSPARENT[3];
  }

  const providerColor = provider === "claude"
    ? hexColor(style.claudeColor, [232, 154, 98, 255])
    : hexColor(style.codexColor, [56, 189, 248, 255]);
  const preset = style.preset;
  const shape = preset === "classic" ? "circle" : style.shape;
  const fill = preset === "classic" || preset === "badge"
    ? "dark"
    : preset === "solid-percent" || preset === "solid-letter"
      ? "solid"
      : preset === "high-contrast"
        ? "dark"
        : preset === "colored-text"
          ? "transparent"
          : style.fill;
  const border = preset === "solid-percent" || preset === "solid-letter"
    ? "none"
    : preset === "high-contrast" || preset === "badge"
      ? "thick"
      : preset === "colored-text"
        ? "none"
        : style.border;
  const background = fill === "solid" ? providerColor : fill === "dark" ? BACKGROUND : TRANSPARENT;
  fillShape(pixels, shape, background);

  if (preset === "classic") {
    drawRing(pixels, percentage, providerColor);
  } else if (border !== "none") {
    drawShapeBorder(pixels, shape, providerColor, background, border === "thick" ? 6 : 3);
  }

  const defaultContent = preset === "solid-letter" ? "provider" : "percent";
  const content = style.content === "auto" ? defaultContent : style.content;
  const label = content === "provider"
    ? provider === "claude" ? "C" : "O"
    : percentage === null
      ? "?"
      : String(Math.round(Math.min(100, Math.max(0, percentage))));
  const textTone = preset === "colored-text" ? "provider" : style.textTone;
  const customTextColor = provider === "claude"
    ? hexColor(style.claudeTextColor, providerColor)
    : hexColor(style.codexTextColor, providerColor);
  const textColor = textTone === "light"
    ? TEXT
    : textTone === "dark"
      ? DARK_TEXT
      : textTone === "provider"
        ? providerColor
        : textTone === "custom"
          ? customTextColor
      : fill === "solid"
        ? contrastText(providerColor)
        : TEXT;
  const maximizeText =
    style.maximizeText || border === "none" || preset === "colored-text";
  const systemFontDrawn = drawSystemTrayFontText(
    pixels,
    WIDTH,
    HEIGHT,
    label,
    textColor,
    style.font,
    maximizeText,
  );
  if (!systemFontDrawn) {
    drawText(
      pixels,
      label,
      textColor,
      FONTS[style.font] ?? FONTS.bold ?? FONTS.classic!,
      maximizeText,
    );
  }

  return nativeImage.createFromBuffer(encodePng(pixels));
}
