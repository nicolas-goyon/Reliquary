import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The API runs on port 5080 in development (see src/Reliquary.Api/Properties/launchSettings.json).
const apiUrl = process.env.RELIQUARY_API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.RELIQUARY_WEB_PORT ?? 5173),
    proxy: {
      '/api': apiUrl,
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
