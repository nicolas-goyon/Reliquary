import { defineConfig } from '@playwright/test'

// The scheduled end-to-end test of the online games. See docs/development/end-to-end-test.md.
// E2E_BASE_URL: the address of the web container. E2E_MINUTES: the duration of the random game.
// E2E_CHROME: the path of a Chrome executable. With no value, Playwright uses its own Chromium.
const minutes = Number(process.env.E2E_MINUTES ?? '10')

export default defineConfig({
  testDir: './tests',
  // The setup, the end of the game and the checks need time after the random game.
  timeout: (minutes + 10) * 60_000,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:8080',
    // The test records the traces itself, in chunks, so a long game does not keep all the actions in memory.
    trace: 'off',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    launchOptions: {
      executablePath: process.env.E2E_CHROME || undefined,
      // The test uses only the local instance, so the browser uses no proxy server.
      args: ['--no-proxy-server'],
    },
  },
})
