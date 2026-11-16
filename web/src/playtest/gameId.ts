/** The format of the ID of a game on one computer: 16 hexadecimal characters. */
export const gameIdPattern = '[0-9a-f]{16}'

/** Gives a random ID for a game with more than one window. */
export function newGameId(): string {
  return Array.from(crypto.getRandomValues(new Uint8Array(8)), (byte) => byte.toString(16).padStart(2, '0')).join('')
}

/**
 * Gives a random seed for a new game.
 * This function is here and not in game.ts, because the modules of the table engine must stay pure (see purity.test.ts).
 */
export function newSeed(): number {
  return crypto.getRandomValues(new Uint32Array(1))[0]
}

/** The path of the player window of a player. */
export function playerWindowPath(gameId: string, playerId: string): string {
  return `/test/games/${gameId}/players/${playerId}`
}

/** The path of the table window of a game. */
export function tableWindowPath(gameId: string): string {
  return `/test/games/${gameId}/table`
}
