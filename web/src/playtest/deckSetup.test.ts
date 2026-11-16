import { describe, expect, it } from 'vitest'
import { testDeck } from '../test/fixtures'
import { playerSetups, usesCommanderDamage } from './deckSetup'

describe('deck setup', () => {
  it('gives each player of a game a different ID and different card IDs, also for the same deck', () => {
    const setups = playerSetups([testDeck(), testDeck(), testDeck({ name: 'Other' })])
    expect(setups.map((setup) => setup.id)).toEqual(['player-1', 'player-2', 'player-3'])
    expect(setups.map((setup) => setup.name)).toEqual(['Atraxa superfriends', 'Atraxa superfriends (2)', 'Other'])
    const ids = setups.flatMap((setup) => [...setup.library, ...setup.command].map((card) => card.id))
    expect(new Set(ids).size).toBe(ids.length)
  })

  it('uses the commander damage for the Commander format or for a deck with a commander only', () => {
    const modern = testDeck({ format: 'modern', lines: testDeck().lines.filter((line) => line.zone !== 'commander') })
    expect(usesCommanderDamage([modern, modern])).toBe(false)
    expect(usesCommanderDamage([modern, testDeck()])).toBe(true)
    expect(usesCommanderDamage([{ ...modern, format: 'oathbreaker', lines: testDeck().lines }])).toBe(true)
  })
})
