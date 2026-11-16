import { describe, expect, it } from 'vitest'
import { formatMessage, handshakeRequest, hubErrorCode, parseHandshakeResponse, parseMessage, splitRecords } from './hubProtocol'

describe('hub protocol', () => {
  it('writes the handshake and the messages with the record separator', () => {
    expect(handshakeRequest()).toBe('{"protocol":"json","version":1}\u001e')
    expect(formatMessage({ type: 1, invocationId: '1', target: 'Join', arguments: [null] })).toBe('{"type":1,"invocationId":"1","target":"Join","arguments":[null]}\u001e')
    expect(formatMessage({ type: 6 })).toBe('{"type":6}\u001e')
  })

  it('splits a frame into records, and keeps the incomplete part', () => {
    expect(splitRecords('{}\u001e{"type":6}\u001e{"ty')).toEqual({ records: ['{}', '{"type":6}'], rest: '{"ty' })
    expect(splitRecords('')).toEqual({ records: [], rest: '' })
  })

  it('reads the handshake response', () => {
    expect(parseHandshakeResponse('{}')).toBeNull()
    expect(parseHandshakeResponse('{"error":"Requested protocol is not available."}')).toBe('Requested protocol is not available.')
    expect(parseHandshakeResponse('not json')).toBe('The handshake response is not JSON.')
  })

  it('reads the messages that the client uses, and ignores the others', () => {
    expect(parseMessage('{"type":1,"target":"Patch","arguments":[{"seq":1}]}')).toEqual({ type: 1, target: 'Patch', arguments: [{ seq: 1 }] })
    expect(parseMessage('{"type":3,"invocationId":"4","result":{"accepted":7}}')).toEqual({ type: 3, invocationId: '4', result: { accepted: 7 } })
    expect(parseMessage('{"type":3,"invocationId":"4","error":"Failed"}')).toEqual({ type: 3, invocationId: '4', error: 'Failed' })
    expect(parseMessage('{"type":6}')).toEqual({ type: 6 })
    expect(parseMessage('{"type":7,"error":"Bye","allowReconnect":true}')).toEqual({ type: 7, error: 'Bye', allowReconnect: true })
    expect(parseMessage('{"type":2,"invocationId":"1","item":1}')).toBeNull()
    expect(parseMessage('{"type":1,"target":5,"arguments":[]}')).toBeNull()
    expect(parseMessage('{')).toBeNull()
  })

  it('reads the error code of a hub method error', () => {
    expect(hubErrorCode("An unexpected error occurred invoking 'Start' on the server. HubException: game_not_enough_players: The game needs 2 players.")).toBe('game_not_enough_players')
    expect(hubErrorCode("An unexpected error occurred invoking 'Kick' on the server. HubException: forbidden")).toBe('forbidden')
    expect(hubErrorCode('Failed')).toBe('internal_error')
  })
})
