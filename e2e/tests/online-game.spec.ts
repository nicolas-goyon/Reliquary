import { writeFile } from 'node:fs/promises'
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test'
import { desyncs, watchPage, type PageWatch } from './hub'
import { linesOf, metricsUrls, scrape, sumOf, hubConnections } from './metrics'
import { createPublicDeck, deckText, hostAccount, secondAccount, type Account } from './setup'
import { randomAction, readTable } from './table'

// The scheduled end-to-end test of the online games: 4 browsers, 2 accounts and 2 guests, against the web container and 2 API replicas.
// See docs/development/end-to-end-test.md.

const minutes = Number(process.env.E2E_MINUTES ?? '10')
const faultInterval = { min: 60_000, max: 120_000 }
const actionPause = { min: 400, max: 2_000 }
const traceChunk = 3 * 60_000

interface Player {
  label: string
  name: string
  context: BrowserContext
  page: Page
  watch: PageWatch
  /** A fault in progress: the player does no action until it ends. */
  busy: Promise<void> | null
  done: number
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))
const between = (range: { min: number; max: number }) => range.min + Math.random() * (range.max - range.min)

/** A small image for each card image. The test does not load the images of the Scryfall servers. */
const cardImage = '<svg xmlns="http://www.w3.org/2000/svg" width="4" height="6"><rect width="4" height="6" fill="#888"/></svg>'

async function newPlayer(browser: Browser, baseURL: string, label: string, name: string): Promise<Player> {
  const context = await browser.newContext({ baseURL })
  const origin = new URL(baseURL).origin
  await context.route(
    (url) => url.origin !== origin,
    (route) => (route.request().resourceType() === 'image' ? route.fulfill({ contentType: 'image/svg+xml', body: cardImage }) : route.continue()),
  )
  await context.tracing.start({ screenshots: true, snapshots: true })
  await context.tracing.startChunk()
  const page = await context.newPage()
  return { label, name, context, page, watch: watchPage(page, label), busy: null, done: 0 }
}

async function signIn(page: Page, account: Account, returnUrl: string) {
  await page.goto(`/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`)
  await page.getByLabel('Public name or email address').fill(account.email)
  await page.getByLabel('Password', { exact: true }).fill(account.password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
}

const hand = (page: Page) => page.getByRole('region', { name: 'Hand', exact: true })

/** Waits until the page shows the own table of the player. */
async function waitForTable(page: Page) {
  await hand(page).waitFor({ timeout: 30_000 })
}

/** A fault: the player reloads the page, or leaves the table and comes back. The seat stays. */
async function fault(player: Player, kind: 'reload' | 'leave') {
  if (kind === 'reload') {
    await player.page.reload()
  } else {
    await player.page.getByRole('button', { name: 'Leave the table' }).click()
    await player.page.getByRole('button', { name: 'Go back to the table' }).click()
  }
  await waitForTable(player.page)
}

async function actor(player: Player, deadline: number) {
  while (Date.now() < deadline) {
    if (player.busy) {
      await player.busy
      continue
    }
    if (await randomAction(player.page)) player.done++
    await sleep(between(actionPause))
  }
}

async function faults(players: Player[], deadline: number, log: string[]) {
  let count = 0
  while (Date.now() + faultInterval.max < deadline) {
    await sleep(between(faultInterval))
    const player = players[Math.floor(Math.random() * players.length)]
    const kind = count++ % 2 === 0 ? 'reload' : 'leave'
    log.push(`${new Date().toISOString()} fault: ${player.label} ${kind}`)
    player.busy = fault(player, kind)
    await player.busy
    player.busy = null
  }
}

/** Keeps only the last chunk of each trace, so a long game does not fill the memory. */
async function rotateTraces(players: Player[], deadline: number) {
  while (Date.now() + traceChunk < deadline) {
    await sleep(traceChunk)
    for (const player of players) {
      await player.context.tracing.stopChunk()
      await player.context.tracing.startChunk()
    }
  }
}

/** Waits until all the pages have the same last sequence number and no action in progress, for 2 seconds. */
async function waitForQuiet(players: Player[]) {
  let stable = 0
  let previous = -1
  for (let attempt = 0; attempt < 120 && stable < 4; attempt++) {
    await sleep(500)
    const seqs = players.map((player) => player.watch.lastSeq)
    const quiet = players.every((player) => player.watch.pendingActs.size === 0) && seqs.every((seq) => seq === seqs[0])
    stable = quiet && seqs[0] === previous ? stable + 1 : 0
    previous = quiet ? seqs[0] : -1
  }
  if (stable < 4) {
    throw new Error(`The pages did not stop: ${players.map((player) => `${player.label} seq ${player.watch.lastSeq}, ${player.watch.pendingActs.size} actions in progress`).join('; ')}`)
  }
}

/** Gives the differences between the views of the pages. Each player must see the same public state of each other player. */
async function compareViews(players: Player[]): Promise<string[]> {
  const tables = await Promise.all(players.map((player) => readTable(player.page)))
  const turns = await Promise.all(players.map((player) => player.page.locator('main .form-actions').first().innerText()))
  const problems: string[] = []
  const turnOf = (text: string) => text.replace(/Next player/, '').replace(/\s+/g, ' ').trim()
  for (let index = 1; index < players.length; index++) {
    if (turnOf(turns[index]) !== turnOf(turns[0])) problems.push(`${players[index].label} shows "${turnOf(turns[index])}", ${players[0].label} shows "${turnOf(turns[0])}".`)
  }
  players.forEach((subject, subjectIndex) => {
    const own = tables[subjectIndex].own
    players.forEach((observer, observerIndex) => {
      if (observerIndex === subjectIndex) return
      const seen = tables[observerIndex].others[subject.name]
      if (!seen) {
        problems.push(`${observer.label} does not show ${subject.name}.`)
        return
      }
      const { counters, ...state } = seen
      if (JSON.stringify(state) !== JSON.stringify(own)) {
        problems.push(`${observer.label} sees ${subject.name} as ${JSON.stringify(state)}, the page of ${subject.label} shows ${JSON.stringify(own)}.`)
      }
      // The board of another player does not show the commander damage from the observer, so the comparison ignores it.
      const first = players.findIndex((_, index) => index !== subjectIndex)
      if (observerIndex === first) return
      const other = (list: string[]) => list.filter((line) => !line.includes(`from ${observer.name}:`) && !line.includes(`from ${players[first].name}:`))
      const reference = tables[first].others[subject.name]?.counters ?? []
      if (JSON.stringify(other(counters)) !== JSON.stringify(other(reference))) {
        problems.push(`${observer.label} sees the counters of ${subject.name} as ${JSON.stringify(counters)}, ${players[first].label} sees ${JSON.stringify(reference)}.`)
      }
    })
  })
  return problems
}

test('an online game with 4 players on 2 API replicas has no desynchronization', async ({ browser, request, baseURL }, testInfo) => {
  if (!baseURL) throw new Error('Set E2E_BASE_URL.')
  const run = Date.now().toString(36)
  const hostDeck = await createPublicDeck(request, hostAccount, `End-to-end test ${run} A`)
  const secondDeck = await createPublicDeck(request, secondAccount, `End-to-end test ${run} B`)

  const players = [
    await newPlayer(browser, baseURL, 'host (account)', 'Host Ana'),
    await newPlayer(browser, baseURL, 'player 2 (account)', 'Player Bea'),
    await newPlayer(browser, baseURL, 'guest 1 (public deck)', 'Guest Cid'),
    await newPlayer(browser, baseURL, 'guest 2 (deck list)', 'Guest Dee'),
  ]
  const [host, second, guestDeck, guestText] = players
  const log: string[] = []
  const summary: string[] = []

  try {
    // 1. The host signs in and creates a game with 4 seats.
    await signIn(host.page, hostAccount, `/decks/${hostDeck}/test/online`)
    await host.page.getByRole('radio', { name: '4 players' }).check()
    await host.page.getByRole('textbox', { name: 'Name in the game (optional)' }).fill(host.name)
    await host.page.getByRole('button', { name: 'Create the game' }).click()
    const inviteField = host.page.getByRole('textbox', { name: 'Invite link' })
    await inviteField.waitFor()
    const invite = await inviteField.inputValue()

    // 2. The second account and the two guests join with the invite link.
    await signIn(second.page, secondAccount, '/')
    await second.page.waitForURL((url) => !url.pathname.startsWith('/sign-in'))
    await second.page.goto(invite)
    await second.page.getByRole('textbox', { name: 'Name in the game (optional)' }).fill(second.name)
    await second.page.getByRole('textbox', { name: 'Deck link' }).fill(new URL(`/decks/${secondDeck}`, baseURL).href)
    await second.page.getByRole('button', { name: 'Join the game' }).click()

    await guestDeck.page.goto(invite)
    await guestDeck.page.getByRole('textbox', { name: 'Nickname' }).fill(guestDeck.name)
    await guestDeck.page.getByRole('textbox', { name: 'Deck link' }).fill(new URL(`/decks/${hostDeck}`, baseURL).href)
    await guestDeck.page.getByRole('button', { name: 'Join the game' }).click()

    await guestText.page.goto(invite)
    await guestText.page.getByRole('textbox', { name: 'Nickname' }).fill(guestText.name)
    await guestText.page.getByRole('radio', { name: 'A deck list' }).check()
    await guestText.page.getByRole('textbox', { name: 'Deck list' }).fill(deckText())
    await guestText.page.getByRole('button', { name: 'Join the game' }).click()

    for (const player of players.slice(1)) await player.page.getByText('The host starts the game.').waitFor()
    const lobby = host.page.getByRole('list', { name: 'Players' })
    for (const player of players.slice(1)) await lobby.getByText(player.name, { exact: true }).waitFor()

    // 3. The players must use the two replicas. Nginx sends each connection to one of the addresses of "api", so a reload can change the replica.
    if (metricsUrls.length >= 2) {
      let spread = false
      for (let attempt = 0; attempt < 12 && !spread; attempt++) {
        await sleep(1_000)
        const connections = await Promise.all(metricsUrls.map(hubConnections))
        log.push(`hub connections on the replicas: ${connections.join(', ')}`)
        spread = connections.every((count) => count > 0)
        if (!spread) {
          const player = players[1 + Math.floor(Math.random() * 3)]
          await player.page.reload()
          await player.page.getByText('The host starts the game.').waitFor()
        }
      }
      expect(spread, 'The players use the two API replicas.').toBe(true)
    } else {
      testInfo.annotations.push({ type: 'warning', description: 'E2E_METRICS_URLS has less than 2 addresses. The test does not check the replicas.' })
    }

    // 4. The host starts the game. Each page shows its own table.
    await host.page.getByRole('button', { name: 'Start the game' }).click()
    for (const player of players) await waitForTable(player.page)

    // 5. Random legal actions of the 4 players for the given duration, with reloads and departures from the table.
    const started = Date.now()
    const deadline = started + minutes * 60_000
    const progress = (async () => {
      while (Date.now() + 60_000 < deadline) {
        await sleep(60_000)
        console.log(`${Math.round((Date.now() - started) / 60_000)} min: ${players.map((player) => `${player.label} ${player.done} actions, seq ${player.watch.lastSeq}`).join('; ')}`)
      }
    })()
    await Promise.all([...players.map((player) => actor(player, deadline)), faults(players, deadline, log), rotateTraces(players, deadline), progress])

    // 6. Checks: no desynchronization, no unknown error, and the same table on all the pages.
    await waitForQuiet(players)
    let problems: string[] = []
    for (let attempt = 0; attempt < 20; attempt++) {
      problems = await compareViews(players)
      if (problems.length === 0) break
      await sleep(500)
    }

    const actions = players.reduce((sum, player) => sum + player.watch.actions, 0)
    summary.push(`Duration: ${minutes} minutes. Actions sent: ${actions}. Last sequence number: ${host.watch.lastSeq}.`)
    for (const player of players) {
      summary.push(
        `${player.label}: ${player.done} actions, ${player.watch.patches} patches, Resync [${player.watch.resyncs.join(', ')}], refused ${JSON.stringify(Object.fromEntries(player.watch.rejected))}`,
      )
    }
    for (const url of metricsUrls) {
      const text = await scrape(url)
      summary.push(`${url}: patches ${sumOf(text, 'reliquary_playtest_patches_total')}, desyncs ${sumOf(text, 'reliquary_playtest_desyncs_total')} ${linesOf(text, 'reliquary_playtest_desyncs_total').join(' ')}`)
      expect(sumOf(text, 'reliquary_playtest_desyncs_total'), `The metric of the desynchronizations of ${url} is 0.`).toBe(0)
      expect(sumOf(text, 'reliquary_playtest_patches_total'), `The replica ${url} sent patches.`).toBeGreaterThan(0)
    }
    console.log([...summary, ...log].join('\n'))

    expect(problems, 'All the pages show the same table.').toEqual([])
    for (const player of players) {
      expect(desyncs(player.watch), `${player.label} called Resync only for a missing view.`).toEqual([])
      expect(player.watch.errors, `${player.label} has no hub, page or console error.`).toEqual([])
      await expect(player.page.getByText(/The page got the full table again/)).toHaveCount(0)
    }

    // 7. The host ends the game. All the pages show the end.
    await host.page.getByRole('button', { name: 'End the game' }).click()
    await host.page.getByRole('button', { name: 'End the game for all' }).click()
    for (const player of players) await player.page.getByText('The game ended.').waitFor()
  } catch (error) {
    // Keep the last chunk of the trace and a screenshot of each page for the investigation.
    for (const player of players) {
      const file = player.label.replace(/\W+/g, '-').replace(/-$/, '')
      await player.context.tracing.stopChunk({ path: testInfo.outputPath(`trace-${file}.zip`) }).catch(() => undefined)
      await player.page.screenshot({ path: testInfo.outputPath(`page-${file}.png`), fullPage: true }).catch(() => undefined)
    }
    console.log([...summary, ...log].join('\n'))
    throw error
  } finally {
    if (process.env.E2E_SUMMARY) await writeFile(process.env.E2E_SUMMARY, [...summary, ...log].map((line) => `- ${line}`).join('\n') + '\n')
    for (const player of players) await player.context.close()
  }
})
