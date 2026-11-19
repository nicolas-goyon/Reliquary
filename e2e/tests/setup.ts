import type { APIRequestContext } from '@playwright/test'

/** A demo account of "demo load". See src/Components/Reliquary.Accounts/DemoAccounts.cs. */
export interface Account {
  email: string
  /** The name on the button of the development sign-in. */
  name: string
}

export const hostAccount: Account = { email: 'demo@reliquary.localhost', name: 'Demo' }
export const secondAccount: Account = { email: 'admin@reliquary.localhost', name: 'Admin' }

// Cards of the demo card data (data/demo). The decks have 1 commander and 44 other cards, so the games use the command zone and the commander damage.
const commander = "Atraxa, Praetors' Voice"
const cards = [
  'Sol Ring', 'Arcane Signet', 'Command Tower', 'Counterspell', 'Brainstorm', 'Cultivate', 'Swords to Plowshares', 'Path to Exile',
  'Demonic Tutor', 'Eternal Witness', 'Birds of Paradise', 'Llanowar Elves', 'Rhystic Study', 'Smothering Tithe', 'Cyclonic Rift',
  'Beast Within', 'Chaos Warp', 'Lightning Greaves', 'Swiftfoot Boots', 'Mind Stone', 'Fellwar Stone', 'Farseek', 'Explore',
]

/** A deck text in the format of the deck import, with 45 cards. */
export function deckText(): string {
  return [`1 ${commander}`, ...cards.map((name) => `1 ${name}`), '21 Forest'].join('\n')
}

async function signIn(request: APIRequestContext, account: Account): Promise<string> {
  // The instance of the test is a local instance, so it has the development sign-in.
  const response = await request.post('/api/v1/auth/development/sign-in', { data: { email: account.email } })
  if (!response.ok()) throw new Error(`The sign-in of ${account.email} failed: ${response.status()} ${await response.text()}`)
  return ((await response.json()) as { accessToken: string }).accessToken
}

/** Creates a public deck of the account and gives its ID. A guest can use a public deck of another user. */
export async function createPublicDeck(request: APIRequestContext, account: Account, name: string): Promise<string> {
  const token = await signIn(request, account)
  const lines = [
    { name: commander, quantity: 1, zone: 'commander' },
    ...cards.map((card) => ({ name: card, quantity: 1, zone: 'main' })),
    { name: 'Forest', quantity: 21, zone: 'main' },
  ]
  const response = await request.post('/api/v1/decks', {
    headers: { Authorization: `Bearer ${token}` },
    data: { name, format: 'commander', visibility: 'public', lines },
  })
  if (!response.ok()) throw new Error(`The deck of ${account.email} was not created: ${response.status()} ${await response.text()}`)
  return ((await response.json()) as { id: string }).id
}
