import { afterEach, describe, expect, it } from 'vitest'
import { deckIdOf, forgetSeat, gamePath, hubUrl, inviteLink, storedSeat, storeSeat } from './gameApi'

describe('online game API', () => {
  afterEach(() => sessionStorage.clear())

  it('makes the hub URL from the page location', () => {
    expect(hubUrl({ protocol: 'https:', host: 'example.org' })).toBe('wss://example.org/api/v1/playtest/hub')
    expect(hubUrl({ protocol: 'http:', host: '192.168.1.5:8080' })).toBe('ws://192.168.1.5:8080/api/v1/playtest/hub')
  })

  it('puts the invite secret in the fragment of the link', () => {
    expect(gamePath('g1')).toBe('/play/g1')
    expect(inviteLink('g1', 'a/b+c', 'https://example.org')).toBe('https://example.org/play/g1#a%2Fb%2Bc')
  })

  it('reads a deck ID from a deck link', () => {
    expect(deckIdOf('https://example.org/decks/0190A000-0000-7000-8000-0000000000D1/test')).toBe('0190a000-0000-7000-8000-0000000000d1')
    expect(deckIdOf('not a link')).toBeNull()
  })

  it('keeps the seat of a game in the tab', () => {
    storeSeat({ gameId: 'g1', seat: 's2', gameToken: 'token' })
    expect(storedSeat('g1')).toEqual({ gameId: 'g1', seat: 's2', gameToken: 'token' })
    expect(storedSeat('g2')).toBeNull()
    forgetSeat('g1')
    expect(storedSeat('g1')).toBeNull()
    sessionStorage.setItem('reliquary.onlineGame.g3', '{"gameId":"g3"}')
    expect(storedSeat('g3')).toBeNull()
  })
})
