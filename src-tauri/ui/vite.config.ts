import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Root and outDir are pinned absolute on purpose: emitted-asset names are
// computed relative to the root, and any cwd/root disagreement (drives,
// junctions, sync mirrors) otherwise surfaces as rollup refusing relative
// or climbing paths. `base: "./"` keeps the bundle relocatable.
const here = fileURLToPath(new URL(".", import.meta.url));

export default defineConfig({
  plugins: [react()],
  root: here,
  base: "./",
  build: { outDir: `${here}dist`, emptyOutDir: true },
  clearScreen: false,
  server: { port: 1420, strictPort: true },
});
