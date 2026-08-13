import { defineConfig } from 'vite'

export default defineConfig({
  base: './',
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    assetsDir: 'assets',
    sourcemap: false,
  },
})
