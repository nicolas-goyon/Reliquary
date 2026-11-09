/** The format of the ID of a game on one computer: 16 hexadecimal characters. */
export const gameIdPattern = '[0-9a-f]{16}'

/** Gives a random ID for a game with more than one window. */
export function newGameId(): string {
  return Array.from(crypto.getRandomValues(new Uint8Array(8)), (byte) => byte.toString(16).padStart(2, '0')).join('')
}

/** The path of the player window of a player. */
export function playerWindowPath(gameId: string, playerId: string): string {
  return `/test/games/${gameId}/players/${playerId}`
}
