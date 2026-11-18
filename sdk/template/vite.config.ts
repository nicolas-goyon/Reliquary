import { readFileSync } from 'node:fs'
import { defineConfig, type Plugin } from 'vite'

/**
 * In the development mode, the core loads the entry point from the development server.
 * This plugin of Vite serves /manifest.json with the entry "src/main.ts", so the file public/manifest.json keeps the entry of the build.
 */
function developmentManifest(): Plugin {
  return {
    name: 'reliquary-development-manifest',
    configureServer(server) {
      server.middlewares.use('/manifest.json', (_request, response) => {
        const manifest = JSON.parse(readFileSync(new URL('./public/manifest.json', import.meta.url), 'utf8')) as Record<string, unknown>
        response.setHeader('Content-Type', 'application/json')
        response.setHeader('Access-Control-Allow-Origin', '*')
        response.end(JSON.stringify({ ...manifest, entry: 'src/main.ts' }))
      })
    },
  }
}

// The plugin is one JavaScript module: dist/index.js. The file public/manifest.json goes to dist/manifest.json.
export default defineConfig({
  plugins: [developmentManifest()],
  server: {
    // The plugin frame has an opaque origin, so its requests have the header "Origin: null". The development server must accept them.
    cors: true,
  },
  build: {
    lib: { entry: 'src/main.ts', formats: ['es'], fileName: () => 'index.js' },
    // A plugin of the catalog must have a reproducible build: one file, with no hash in its name.
    rollupOptions: { output: { inlineDynamicImports: true } },
  },
})
