import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

// The editor runtime is a second, deliberately unbundled-from-the-app
// entry: a single fixed-name IIFE file that the Write pipeline injects
// into the artifact page (`<script src="/tezuri-editor.js">`). No hashing:
// the injected name is part of the protocol.
const here = fileURLToPath(new URL(".", import.meta.url));

export default defineConfig({
  root: here,
  base: "./",
  build: {
    outDir: `${here}dist`,
    emptyOutDir: false,
    rollupOptions: {
      input: `${here}src/editor-runtime.ts`,
      output: {
        format: "iife",
        entryFileNames: "tezuri-editor.js",
        assetFileNames: "tezuri-editor-[name][extname]",
      },
    },
  },
  clearScreen: false,
});
