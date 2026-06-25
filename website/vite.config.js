import { defineConfig } from 'vite'

// Static marketing site. No framework — just Vite for bundling, dev server,
// and asset hashing. Output goes to dist/ for Netlify.
export default defineConfig({
  root: '.',
  base: '/',
  build: {
    outDir: 'dist',
    target: 'es2020',
    cssCodeSplit: false,
    assetsInlineLimit: 2048,
  },
  server: {
    port: 5173,
    host: true,
  },
  preview: {
    port: 4173,
    host: true,
  },
})
