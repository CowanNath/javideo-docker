import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import UnoCSS from 'unocss/vite'
import { fileURLToPath, URL } from 'node:url'

// Web build: the worker serves the built SPA from ./wwwroot (same origin, no
// CORS involved). During `vite dev` the same-origin '' base goes through the
// /api proxy below — point it at a locally running worker (the docker
// container works too: VITE_DEV_WORKER_ORIGIN=http://localhost:8080).
const devWorker = process.env.VITE_DEV_WORKER_ORIGIN || 'http://127.0.0.1:8080'

export default defineConfig({
  plugins: [vue(), UnoCSS()],
  clearScreen: false,
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 1420,
    strictPort: true,
    proxy: {
      '/api': devWorker,
    },
  },
  build: {
    target: 'esnext',
    outDir: 'dist',
  },
  // Served from the worker's site root (wwwroot) — absolute base.
  base: '/',
})
