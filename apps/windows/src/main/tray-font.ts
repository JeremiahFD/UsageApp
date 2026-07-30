import type { TrayIconFont } from "@usageapp/core";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { parse, type Font, type PathCommand } from "opentype.js";

type Color = readonly [number, number, number, number];
type SystemTrayIconFont = Exclude<
  TrayIconFont,
  "classic" | "bold" | "rounded"
>;

interface Point {
  x: number;
  y: number;
}

const FONT_FILES: Record<SystemTrayIconFont, readonly string[]> = {
  "segoe-ui": ["seguisb.ttf", "segoeuib.ttf", "segoeui.ttf"],
  verdana: ["verdanab.ttf", "verdana.ttf"],
  tahoma: ["tahomabd.ttf", "tahoma.ttf"],
  arial: ["arialbd.ttf", "arial.ttf"],
  "trebuchet-ms": ["trebucbd.ttf", "trebuc.ttf"],
  georgia: ["georgiab.ttf", "georgia.ttf"],
  consolas: ["consolab.ttf", "consola.ttf"],
};

const fontCache = new Map<SystemTrayIconFont, Font | null>();

function isSystemTrayIconFont(
  value: TrayIconFont,
): value is SystemTrayIconFont {
  return value in FONT_FILES;
}

function windowsFontsDirectory(): string {
  const windowsDirectory = process.env.WINDIR?.trim() || "C:\\Windows";
  return join(windowsDirectory, "Fonts");
}

function loadFont(choice: SystemTrayIconFont): Font | null {
  const cached = fontCache.get(choice);
  if (cached !== undefined) return cached;

  for (const fileName of FONT_FILES[choice]) {
    try {
      const buffer = readFileSync(join(windowsFontsDirectory(), fileName));
      const arrayBuffer = buffer.buffer.slice(
        buffer.byteOffset,
        buffer.byteOffset + buffer.byteLength,
      ) as ArrayBuffer;
      const font = parse(arrayBuffer);
      fontCache.set(choice, font);
      return font;
    } catch {
      // Try the next normal Windows filename before falling back.
    }
  }

  fontCache.set(choice, null);
  return null;
}

function quadraticPoint(
  start: Point,
  control: Point,
  end: Point,
  amount: number,
): Point {
  const inverse = 1 - amount;
  return {
    x: inverse * inverse * start.x +
      2 * inverse * amount * control.x +
      amount * amount * end.x,
    y: inverse * inverse * start.y +
      2 * inverse * amount * control.y +
      amount * amount * end.y,
  };
}

function cubicPoint(
  start: Point,
  firstControl: Point,
  secondControl: Point,
  end: Point,
  amount: number,
): Point {
  const inverse = 1 - amount;
  return {
    x: inverse ** 3 * start.x +
      3 * inverse * inverse * amount * firstControl.x +
      3 * inverse * amount * amount * secondControl.x +
      amount ** 3 * end.x,
    y: inverse ** 3 * start.y +
      3 * inverse * inverse * amount * firstControl.y +
      3 * inverse * amount * amount * secondControl.y +
      amount ** 3 * end.y,
  };
}

function flattenPath(commands: PathCommand[]): Point[][] {
  const contours: Point[][] = [];
  let contour: Point[] = [];
  let current: Point = { x: 0, y: 0 };

  const finishContour = (): void => {
    if (contour.length >= 3) contours.push(contour);
    contour = [];
  };

  for (const command of commands) {
    if (command.type === "M") {
      finishContour();
      current = { x: command.x, y: command.y };
      contour.push(current);
      continue;
    }

    if (command.type === "L") {
      current = { x: command.x, y: command.y };
      contour.push(current);
      continue;
    }

    if (command.type === "Q") {
      const start = current;
      const control = { x: command.x1, y: command.y1 };
      const end = { x: command.x, y: command.y };
      for (let step = 1; step <= 12; step += 1) {
        contour.push(quadraticPoint(start, control, end, step / 12));
      }
      current = end;
      continue;
    }

    if (command.type === "C") {
      const start = current;
      const firstControl = { x: command.x1, y: command.y1 };
      const secondControl = { x: command.x2, y: command.y2 };
      const end = { x: command.x, y: command.y };
      for (let step = 1; step <= 16; step += 1) {
        contour.push(cubicPoint(
          start,
          firstControl,
          secondControl,
          end,
          step / 16,
        ));
      }
      current = end;
      continue;
    }

    if (command.type === "Z") {
      finishContour();
    }
  }

  finishContour();
  return contours;
}

function rasterizeContours(
  contours: Point[][],
  width: number,
  height: number,
  oversample = 4,
): Uint8Array {
  const sampleWidth = width * oversample;
  const sampleHeight = height * oversample;
  const samples = new Uint8Array(sampleWidth * sampleHeight);

  for (let sampleY = 0; sampleY < sampleHeight; sampleY += 1) {
    const y = (sampleY + 0.5) / oversample;
    const crossings: Array<{ x: number; winding: number }> = [];

    for (const contour of contours) {
      for (let index = 0; index < contour.length; index += 1) {
        const start = contour[index];
        const end = contour[(index + 1) % contour.length];
        if (!start || !end || start.y === end.y) continue;
        const crosses =
          (start.y <= y && end.y > y) ||
          (end.y <= y && start.y > y);
        if (!crosses) continue;
        const amount = (y - start.y) / (end.y - start.y);
        crossings.push({
          x: start.x + amount * (end.x - start.x),
          winding: end.y > start.y ? 1 : -1,
        });
      }
    }

    crossings.sort((left, right) => left.x - right.x);
    let winding = 0;
    let previousX = 0;
    for (const crossing of crossings) {
      if (winding !== 0) {
        const startX = Math.max(
          0,
          Math.ceil(previousX * oversample - 0.5),
        );
        const endX = Math.min(
          sampleWidth,
          Math.ceil(crossing.x * oversample - 0.5),
        );
        if (endX > startX) {
          samples.fill(1, sampleY * sampleWidth + startX, sampleY * sampleWidth + endX);
        }
      }
      winding += crossing.winding;
      previousX = crossing.x;
    }
  }

  const mask = new Uint8Array(width * height);
  const samplesPerPixel = oversample * oversample;
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      let coverage = 0;
      for (let sampleY = 0; sampleY < oversample; sampleY += 1) {
        const row = (y * oversample + sampleY) * sampleWidth;
        for (let sampleX = 0; sampleX < oversample; sampleX += 1) {
          coverage += samples[row + x * oversample + sampleX] ?? 0;
        }
      }
      mask[y * width + x] = Math.round(
        (coverage / samplesPerPixel) * 255,
      );
    }
  }
  return mask;
}

function blendPixel(
  pixels: Uint8Array,
  index: number,
  color: Color,
  coverage: number,
): void {
  if (coverage <= 0) return;
  const sourceAlpha = (coverage / 255) * (color[3] / 255);
  const destinationAlpha = (pixels[index + 3] ?? 0) / 255;
  const outputAlpha =
    sourceAlpha + destinationAlpha * (1 - sourceAlpha);
  if (outputAlpha <= 0) return;

  for (let channel = 0; channel < 3; channel += 1) {
    const source = (color[channel] ?? 0) / 255;
    const destination = (pixels[index + channel] ?? 0) / 255;
    pixels[index + channel] = Math.round(
      ((source * sourceAlpha) +
        (destination * destinationAlpha * (1 - sourceAlpha))) /
        outputAlpha *
        255,
    );
  }
  pixels[index + 3] = Math.round(outputAlpha * 255);
}

export function drawSystemTrayFontText(
  pixels: Uint8Array,
  width: number,
  height: number,
  value: string,
  color: Color,
  fontChoice: TrayIconFont,
  maximize: boolean,
): boolean {
  if (!isSystemTrayIconFont(fontChoice)) return false;
  const font = loadFont(fontChoice);
  if (!font) return false;

  const initialSize = 64;
  const initialPath = font.getPath(value, 0, 0, initialSize);
  const initialBox = initialPath.getBoundingBox();
  const initialWidth = Math.max(1, initialBox.x2 - initialBox.x1);
  const initialHeight = Math.max(1, initialBox.y2 - initialBox.y1);
  const targetWidth = maximize ? width - 2 : width - 12;
  const targetHeight = maximize ? height - 4 : height - 14;
  const fontSize = initialSize * Math.min(
    targetWidth / initialWidth,
    targetHeight / initialHeight,
  );

  const centeredPath = font.getPath(value, 0, 0, fontSize);
  const box = centeredPath.getBoundingBox();
  const offsetX = (width - (box.x2 - box.x1)) / 2 - box.x1;
  const offsetY = (height - (box.y2 - box.y1)) / 2 - box.y1;
  const path = font.getPath(value, offsetX, offsetY, fontSize);
  const mask = rasterizeContours(flattenPath(path.commands), width, height);

  for (let pixelIndex = 0; pixelIndex < mask.length; pixelIndex += 1) {
    blendPixel(pixels, pixelIndex * 4, color, mask[pixelIndex] ?? 0);
  }
  return true;
}
