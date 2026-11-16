import type { Page } from '@playwright/test'

/** The kinds of controls of the own table of a player. */
export type ControlKind = 'card' | 'draw' | 'mulligan' | 'shuffle' | 'untapAll' | 'life' | 'counter' | 'token' | 'nextTurn'

/**
 * The relative weights of the kinds. Most actions change cards, as in a real game.
 * Tokens stay on the battlefield, so the test makes few of them.
 */
const weights: Record<ControlKind, number> = {
  card: 55,
  draw: 8,
  mulligan: 1,
  shuffle: 2,
  untapAll: 4,
  life: 6,
  counter: 6,
  token: 2,
  nextTurn: 6,
}

const toolbarKinds: ControlKind[] = ['draw', 'mulligan', 'shuffle', 'untapAll']

/**
 * Marks the controls of the own table with the attribute data-e2e, and counts them by kind.
 * The own table is the part of the page before the section "Other players". The buttons to leave and to end the game come after it.
 */
async function markControls(page: Page): Promise<Partial<Record<ControlKind, number>>> {
  return page.evaluate((kinds) => {
    for (const element of document.querySelectorAll('[data-e2e]')) element.removeAttribute('data-e2e')
    const others = document.querySelector('section[aria-labelledby$="-others"]')
    const counts: Record<string, number> = {}
    if (!others) return counts
    for (const element of document.querySelectorAll<HTMLElement>('main button, main select')) {
      if (!(others.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_PRECEDING)) continue
      let kind: string | null = null
      const toolbar = element.closest('[role="toolbar"]')
      if (element.closest('.table-card')) kind = 'card'
      else if (toolbar) kind = kinds[Array.from(toolbar.querySelectorAll('button')).indexOf(element as HTMLButtonElement)] ?? null
      else if (element.closest('.table-life')) kind = 'life'
      else if (element.closest('.table-counters')) kind = 'counter'
      else if (element.closest('form')) kind = 'token'
      else if (element.classList.contains('button-primary')) kind = 'nextTurn'
      if (kind && !(element as HTMLButtonElement).disabled) {
        element.setAttribute('data-e2e', kind)
        counts[kind] = (counts[kind] ?? 0) + 1
      }
    }
    return counts
  }, toolbarKinds)
}

function pick<T>(items: T[], weight: (item: T) => number): T {
  let rest = Math.random() * items.reduce((sum, item) => sum + weight(item), 0)
  for (const item of items) {
    rest -= weight(item)
    if (rest <= 0) return item
  }
  return items[items.length - 1]
}

let tokenNumber = 0

/**
 * Does one random action of the player with a control of its own table. Each control of the own table is a legal action of this player.
 * Gives the kind of the action, or null when the control went out of the page before the click.
 */
export async function randomAction(page: Page): Promise<ControlKind | null> {
  const counts = await markControls(page)
  const kinds = (Object.keys(counts) as ControlKind[]).filter((kind) => (counts[kind] ?? 0) > 0)
  if (kinds.length === 0) return null
  const kind = pick(kinds, (item) => weights[item])
  const control = page.locator(`[data-e2e="${kind}"]`).nth(Math.floor(Math.random() * (counts[kind] ?? 1)))
  try {
    if (kind === 'token') {
      await control.locator('xpath=ancestor::form//input').fill(`Token ${++tokenNumber}`, { timeout: 3_000 })
    }
    const tag = await control.evaluate((element) => element.tagName, undefined, { timeout: 3_000 })
    if (tag === 'SELECT') {
      const values = await control.evaluate((element) => Array.from((element as HTMLSelectElement).options, (option) => option.value).filter(Boolean))
      await control.selectOption(values[Math.floor(Math.random() * values.length)], { timeout: 3_000 })
    } else {
      await control.click({ timeout: 3_000 })
    }
    return kind
  } catch {
    // A patch changed the table between the count and the click. This is normal.
    return null
  }
}

/** The public state of one player: the hidden zones as counts, and the cards of the public zones. */
export interface PublicPlayer {
  life: string
  hand: number
  library: number
  zones: Record<string, string[]>
}

/** The zones that all the players see. */
const publicZones = ['Battlefield', 'Command zone', 'Graveyard', 'Exile']

/**
 * Reads the table of a page: the own player from its controls, and each other player from the section "Other players".
 * A card is "name|tapped|counters", or "back" for a face-down card. The order of the cards is the order of the zone.
 * The counters of another player are the texts of its player counters and of its commander damage.
 */
export async function readTable(page: Page): Promise<{ own: PublicPlayer; others: Record<string, PublicPlayer & { counters: string[] }> }> {
  return page.evaluate((zones) => {
    const describe = (card: Element) => {
      const image = card.querySelector('.card-image')
      const back = card.querySelector('.table-card-back')
      const name = back ? 'back' : (image?.getAttribute('alt') ?? image?.getAttribute('aria-label') ?? '?').replace(/, tapped$/, '')
      const badge = card.querySelector('.badge')?.textContent ?? ''
      return `${name}|${card.classList.contains('table-card-tapped')}|${badge}`
    }
    const cardsOf = (section: Element | null) => (section ? Array.from(section.querySelectorAll('li.table-card'), describe) : [])
    const others = document.querySelector('section[aria-labelledby$="-others"]')
    const main = document.querySelector('main')!
    const ownZone = (zone: string) =>
      Array.from(main.querySelectorAll(`section[aria-label="${zone}"]`)).find((section) => !others || others.compareDocumentPosition(section) & Node.DOCUMENT_POSITION_PRECEDING) ?? null
    const drawText = main.querySelector('[role="toolbar"] button')?.textContent ?? ''
    const own = {
      life: main.querySelector('span[aria-label="Life points"]')?.textContent ?? '',
      hand: ownZone('Hand')?.querySelectorAll('li.table-card').length ?? -1,
      library: Number(/\((\d+)\)/.exec(drawText)?.[1] ?? -1),
      zones: Object.fromEntries(zones.map((zone) => [zone, cardsOf(ownZone(zone))])),
    }
    const result: Record<string, { life: string; hand: number; library: number; zones: Record<string, string[]>; counters: string[] }> = {}
    for (const board of others?.querySelectorAll('section.table-public-player') ?? []) {
      const name = board.getAttribute('aria-label') ?? ''
      const counts = Array.from(board.querySelectorAll('.table-hidden-counts dd'), (item) => Number(item.textContent))
      result[name] = {
        life: /(-?\d+)/.exec(board.querySelector('.table-life')?.textContent ?? '')?.[1] ?? '',
        hand: counts[0] ?? -1,
        library: counts[1] ?? -1,
        zones: Object.fromEntries(zones.map((zone) => [zone, cardsOf(board.querySelector(`section[aria-label="${zone} of ${name}"]`))])),
        counters: Array.from(board.querySelectorAll('.table-public-counters li'), (item) => (item.textContent ?? '').trim()),
      }
    }
    return { own, others: result }
  }, publicZones)
}
