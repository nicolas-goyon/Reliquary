import { lineImage, type Deck } from '../api/decks'
import type { PlayerSetup } from './engine'

/** The minimum and the maximum number of decks of a local multiplayer game. */
export const localPlayers = { min: 3, max: 4 } as const

/** Tells if a game with these decks uses the commander damage: a deck of the Commander format, or a deck with a commander. */
export function usesCommanderDamage(decks: readonly Deck[]): boolean {
  return decks.some((deck) => deck.format === 'commander' || deck.lines.some((line) => line.zone === 'commander'))
}

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

/**
 * Makes the setups of a game with many decks. The player IDs are "player-1", "player-2" and so on, in the order of the decks.
 * The same deck can be in the game two times: each player has different card IDs.
 */
export function playerSetups(decks: readonly Deck[]): PlayerSetup[] {
  return decks.map((deck, index) => {
    // Two players with the same deck get different names: "Name", then "Name (2)".
    const copy = decks.slice(0, index).filter((other) => other.name === deck.name).length + 1
    return playerSetup(deck, `player-${index + 1}`, copy > 1 ? `${deck.name} (${copy})` : deck.name)
  })
}
