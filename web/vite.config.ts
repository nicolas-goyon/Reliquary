import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'
import { readFileSync } from 'node:fs'
import { serviceWorker } from './vite.service-worker.ts'

// The API runs on port 5080 in development (see src/Reliquary.Api/Properties/launchSettings.json).
const apiUrl = process.env.RELIQUARY_API_URL ?? 'http://localhost:5080'

// The version that the web app shows. The CI gives it to the container build.
const packageJson = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8')) as { version: string }
const version = process.env.RELIQUARY_VERSION || packageJson.version

export default defineConfig({
  plugins: [react(), serviceWorker()],
  define: {
    __RELIQUARY_VERSION__: JSON.stringify(version),
  },
  server: {
    port: Number(process.env.RELIQUARY_WEB_PORT ?? 5173),
    proxy: {
      '/api': apiUrl,
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Some page tests take 3 to 4 seconds alone with user-event, and more when all the test files run in parallel.
    testTimeout: 15000,
    // The tests of the theme tokens read the main style sheet. Vitest gives an empty module for other CSS files.
    css: { include: [/src\/styles\.css/] },
  },
})
