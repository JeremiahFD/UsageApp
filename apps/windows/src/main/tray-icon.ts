import { nativeImage, type NativeImage } from "electron";
import { deflateSync } from "node:zlib";

type Color = readonly [number, number, number, number];

const WIDTH = 64;
const HEIGHT = 64;
const TRANSPARENT: Color = [0, 0, 0, 0];
const BACKGROUND: Color = [17, 24, 39, 255];
const TRACK: Color = [68, 79, 96, 255];
const TEXT: Color = [248, 250, 252, 255];

const GLYPHS: Record<string, readonly string[]> = {
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
};

function progressColor(percentage: number | null): Color {
  if (percentage === null) return [148, 163, 184, 255];
  if (percentage <= 15) return [248, 113, 113, 255];
  if (percentage <= 40) return [251, 191, 36, 255];
  return [56, 189, 248, 255];
}

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

function drawRing(
  pixels: Uint8Array,
  percentage: number | null,
): void {
  const center = 31.5;
  const innerSquared = 26 * 26;
  const outerSquared = 31 * 31;
  const fraction =
    percentage === null ? 0 : Math.min(100, Math.max(0, percentage)) / 100;
  const active = progressColor(percentage);

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
): void {
  const scale = value.length >= 3 ? 4 : 5;
  const gap = scale;
  const glyphWidth = 3 * scale;
  const totalWidth = value.length * glyphWidth + (value.length - 1) * gap;
  const startX = Math.round((WIDTH - totalWidth) / 2);
  const startY = Math.round((HEIGHT - 5 * scale) / 2);

  [...value].forEach((character, characterIndex) => {
    const glyph = GLYPHS[character] ?? GLYPHS["?"];
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

export function createTrayIcon(percentage: number | null): NativeImage {
  const pixels = new Uint8Array(WIDTH * HEIGHT * 4);
  for (let index = 0; index < pixels.length; index += 4) {
    pixels[index] = TRANSPARENT[0];
    pixels[index + 1] = TRANSPARENT[1];
    pixels[index + 2] = TRANSPARENT[2];
    pixels[index + 3] = TRANSPARENT[3];
  }

  fillCircle(pixels, 32, 32, 27, BACKGROUND);
  drawRing(pixels, percentage);
  const label =
    percentage === null
      ? "?"
      : String(Math.round(Math.min(100, Math.max(0, percentage))));
  drawText(pixels, label, TEXT);

  return nativeImage.createFromBuffer(encodePng(pixels));
}
