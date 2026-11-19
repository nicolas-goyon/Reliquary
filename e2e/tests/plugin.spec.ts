import { createReadStream, existsSync } from 'node:fs'
import { createServer, type Server } from 'node:http'
import type { AddressInfo } from 'node:net'
import { join, normalize } from 'node:path'
import { expect, test } from '@playwright/test'
import { createPublicDeck, hostAccount } from './setup'

// The build of the plugin template. See sdk/template/README.md.
const templateBuild = process.env.E2E_PLUGIN_DIST ?? join(import.meta.dirname, '..', '..', 'sdk', 'template', 'dist')
const contentTypes: Record<string, string> = { '.js': 'text/javascript', '.json': 'application/json' }

/** Serves the build of the plugin template. The plugin frame has an opaque origin, so the server accepts all the origins. */
function serveTemplate(): Promise<Server> {
  const server = createServer((request, response) => {
    const path = normalize(new URL(request.url ?? '/', 'http://localhost').pathname)
    const file = join(templateBuild, path)
    const type = contentTypes[path.slice(path.lastIndexOf('.'))]
    if (!file.startsWith(templateBuild) || !type || !existsSync(file)) {
      response.writeHead(404).end()
      return
    }
    response.writeHead(200, { 'Content-Type': type, 'Access-Control-Allow-Origin': '*', 'Cache-Control': 'no-store' })
    createReadStream(file).pipe(response)
  })
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(server)))
}

test('the plugin template runs in a real frame and reads a deck', async ({ page, request }) => {
  test.setTimeout(120_000)
  expect(existsSync(join(templateBuild, 'index.js')), `The build of the plugin template is in ${templateBuild}.`).toBe(true)
  const server = await serveTemplate()
  const manifestUrl = `http://localhost:${(server.address() as AddressInfo).port}/manifest.json`
  const errors: string[] = []
  page.on('pageerror', (error) => errors.push(error.message))

  try {
    const deck = await createPublicDeck(request, hostAccount, 'Plugin test')

    await page.goto('/sign-in?returnUrl=%2Faccount')
    await page.getByRole('button', { name: `Open the account ${hostAccount.name}`, exact: true }).click()
    await page.getByRole('textbox', { name: 'URL of the plugin manifest' }).fill(manifestUrl)
    await page.getByRole('button', { name: 'Load the plugin' }).click()
    await expect(page.getByText('The plugin "Deck size" runs now in this browser.')).toBeVisible()

    await page.goto(`/decks/${deck}`)
    await expect(page.getByRole('heading', { level: 1, name: 'Plugin test' })).toBeVisible()

    // 1. The action of the plugin: the hidden frame reads the deck with the public API, and the core shows the result.
    await page.getByRole('button', { name: 'Count the cards' }).click()
    await expect(page.getByText('Plugin test: 45 cards.').first()).toBeVisible()

    // 2. The panel of the plugin: a visible frame in the tabs of the deck analysis.
    await page.getByRole('tab', { name: 'Deck size' }).click()
    const frame = page.locator('iframe[title="Deck size"]')
    // The sandbox gives an opaque origin to the frame: the plugin cannot read the page, the cookies or the storage of the core.
    await expect(frame).toHaveAttribute('sandbox', 'allow-scripts')
    await expect(frame.contentFrame().getByText('Plugin test: 45 cards.')).toBeVisible()

    expect(errors, 'The page has no error.').toEqual([])
  } finally {
    server.close()
  }
})
