import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";
import { fileURLToPath } from "node:url";

const workspaceReact = fileURLToPath(
  new URL("../../node_modules/react", import.meta.url),
);
const workspaceReactDom = fileURLToPath(
  new URL("../../node_modules/react-dom", import.meta.url),
);

export default defineConfig({
  plugins: [react()],
  base: "./",
  resolve: {
    // The hoisted Expo/desktop monorepo shares React at the workspace root.
    // pnpm's virtual store also contains peer-only React variants for Expo;
    // pin the desktop renderer to the public root pair so Rollup never reaches
    // a different react-dom reconciler through Vite's own dependency path.
    alias: {
      react: workspaceReact,
      "react-dom": workspaceReactDom,
    },
    dedupe: ["react", "react-dom"],
  },
  build: {
    outDir: "dist/renderer",
    emptyOutDir: false,
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
  },
});
