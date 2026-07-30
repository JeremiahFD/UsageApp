import { defineConfig } from "tsup";

export default defineConfig({
  entry: {
    index: "src/main/index.ts",
    preload: "src/main/preload.ts",
  },
  format: ["cjs"],
  platform: "node",
  target: "node22",
  outDir: "dist/main",
  external: ["electron"],
  noExternal: ["@usageapp/core", "opentype.js"],
  sourcemap: true,
  splitting: false,
  clean: false,
});
