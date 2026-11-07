import { lineImage, type Deck } from '../api/decks'
import type { PlayerSetup } from './engine'

/** The life points at the start of a game of a format. */
export function startingLife(format: string): number {
  return format === 'commander' ? 40 : 20
}

/** Makes the setup of a player from a deck. The sideboard stays out of the game. */
export function playerSetup(deck: Deck, id: string, name: string): PlayerSetup {
  const copies = (zones: string[]) =>
    deck.lines
      .filter((line) => zones.includes(line.zone))
      .flatMap((line) => {
        const card = deck.cards[line.oracleId]
        return Array.from({ length: line.quantity }, (_, index) => ({
          id: `${id}-${line.id}-${index}`,
          oracleId: line.oracleId,
          name: card?.name ?? line.oracleId,
          imageUri: lineImage(deck, line) ?? null,
          isLand: card?.isLand ?? false,
          manaValue: card?.manaValue ?? 0,
        }))
      })
  return { id, name, library: copies(['main']), command: copies(['commander', 'companion']) }
}
