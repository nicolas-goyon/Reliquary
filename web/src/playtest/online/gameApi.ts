import type { RequestOptions } from '../../api/client'
import type { PlayerView } from '../views'
import type { HubClientOptions } from './hubClient'

/*
 * The contract of the online games between the web app and the API: the REST endpoints and the shapes of the hub messages.
 * The source is docs/architecture/online-games.md (REST endpoints, #260), the approved design of epic #54 (section 3) and ADR 0047.
 * The hub (#233) uses the message names of hubClient.ts. When a shape changes, change this file only.
 * The REST calls with a game token (lobby, invite, kick, close) send it in the header X-Game-Token. The pages use the hub for these actions.
 */

/** Sends a request to the API. It is the function `request` of useAuth: it adds the access token when the user is signed in. */
export type ApiRequest = <T>(path: string, options?: Omit<RequestOptions, 'accessToken'>) => Promise<T>

/** The number of seats of an online game. */
export const onlineSeats = { min: 2, max: 4 } as const

/** The limits of the join form. The API checks them again. */
export const joinLimits = { nicknameMax: 24, deckTextMax: 10 * 1024 } as const

/** Reads a deck ID from a deck link or from the ID itself. */
export function deckIdOf(value: string): string | null {
  return /([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i.exec(value.trim())?.[1].toLowerCase() ?? null
}

/** The game token of one seat. The hub accepts only this token. */
export interface SeatAccess {
  gameId: string
  /** The ID of the seat: "0" to "3". It is also the ID of the player in the views. The host has the seat "0". */
  seat: string
  gameToken: string
}

/** The answer to the creation of a game. The host gets a seat and the secret of the invite link. */
export interface CreatedGame extends SeatAccess {
  inviteSecret: string
}

/** The deck of a player who joins. A guest gives a public deck or a deck list. */
export type JoinDeck = { deckId: string } | { deckText: string }

/**
 * Creates a game with a deck of the user. Only an account can create a game.
 * @param nickname The name of the host in the game. With no nickname, the API uses the display name of the account.
 */
export function createGame(request: ApiRequest, deckId: string, seats: number, nickname?: string): Promise<CreatedGame> {
  return request<CreatedGame>('/playtest/games', { method: 'POST', body: { deckId, seats, ...(nickname ? { nickname } : {}) } })
}

/**
 * Joins a game with the invite secret. A guest must give a nickname.
 * @param nickname The name of the player in the game. With no nickname, the API uses the display name of the account.
 */
export function joinGame(request: ApiRequest, gameId: string, inviteSecret: string, deck: JoinDeck, nickname?: string): Promise<SeatAccess> {
  return request<SeatAccess>(`/playtest/games/${encodeURIComponent(gameId)}/join`, { method: 'POST', body: { inviteSecret, ...deck, ...(nickname ? { nickname } : {}) } })
}

/** Makes the WebSocket of the hub. The tests replace it with a fake socket. */
export const hubSocket: { create: HubClientOptions['createSocket'] } = { create: undefined }

/** The URL of the hub. The hub client adds the game token. */
export function hubUrl(location: Pick<Location, 'protocol' | 'host'> = window.location): string {
  return `${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/api/v1/playtest/hub`
}

/** The path of the page of a game. */
export function gamePath(gameId: string): string {
  return `/play/${encodeURIComponent(gameId)}`
}

/** The invite link. The secret is in the fragment, so that the servers do not write it in their logs. */
export function inviteLink(gameId: string, inviteSecret: string, origin: string = window.location.origin): string {
  return `${origin}${gamePath(gameId)}#${encodeURIComponent(inviteSecret)}`
}

/** One seat of the lobby. */
export interface LobbySeat {
  seat: string
  nickname: string
  /** Always true: a seat exists only with a deck. */
  ready: boolean
  guest?: boolean
  cardCount?: number
}

/** The message "Lobby" of the hub. */
export interface Lobby {
  seats: LobbySeat[]
  hostSeat: string
  /** The number of seats of the game. */
  size: number
  started: boolean
}

/** The snapshot of the hub methods Join and Resync. Before the start of the game, the view is null. */
export interface OnlineSnapshot {
  seq: number
  view: PlayerView | null
  hash: string
}

/** The answer of the hub method NewInvite. */
export interface NewInvite {
  inviteSecret: string
}

const storagePrefix = 'reliquary.onlineGame.'

/**
 * Keeps the seat of the user in the tab, so a reload of the page connects to the same seat.
 * The browser storage can fail, for example in a private window: then the page works with no reload support.
 */
export function storeSeat(access: SeatAccess & { inviteSecret?: string }): void {
  try {
    window.sessionStorage.setItem(storagePrefix + access.gameId, JSON.stringify(access))
  } catch {
    // No storage: a reload asks the user to join again.
  }
}

/** Gives the seat that storeSeat kept for a game, or null. */
export function storedSeat(gameId: string): (SeatAccess & { inviteSecret?: string }) | null {
  try {
    const value: unknown = JSON.parse(window.sessionStorage.getItem(storagePrefix + gameId) ?? 'null')
    if (value && typeof value === 'object') {
      const access = value as Record<string, unknown>
      if (access.gameId === gameId && typeof access.seat === 'string' && typeof access.gameToken === 'string') {
        return { gameId, seat: access.seat, gameToken: access.gameToken, ...(typeof access.inviteSecret === 'string' ? { inviteSecret: access.inviteSecret } : {}) }
      }
    }
  } catch {
    // No storage or a wrong value.
  }
  return null
}

/** Removes the seat of a game from the tab. */
export function forgetSeat(gameId: string): void {
  try {
    window.sessionStorage.removeItem(storagePrefix + gameId)
  } catch {
    // No storage.
  }
}
