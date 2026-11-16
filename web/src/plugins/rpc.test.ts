import { afterEach, describe, expect, it } from 'vitest'
import { errorCodes } from './protocol'
import { RpcError, RpcPeer, utf8Length, type PortLike } from './rpc'

type Methods = {
  [method: string]: { params: unknown; result: unknown }
  echo: { params: { text: string }; result: { text: string } }
  fail: { params: null; result: null }
  never: { params: null; result: null }
}

const peers: RpcPeer<Methods, Methods>[] = []

function connectedPeers(options: { timeoutMs?: number; maxMessageBytes?: number } = {}) {
  const channel = new MessageChannel()
  const left = new RpcPeer<Methods, Methods>(channel.port1 as unknown as PortLike, options)
  const right = new RpcPeer<Methods, Methods>(channel.port2 as unknown as PortLike, options)
  peers.push(left, right)
  return { left, right, channel }
}

afterEach(() => {
  for (const peer of peers.splice(0)) {
    peer.close()
  }
})

describe('RpcPeer', () => {
  it('gets the result of a call', async () => {
    const { left, right } = connectedPeers()
    right.handle('echo', ({ text }) => ({ text: text.toUpperCase() }))

    expect(await left.call('echo', { text: 'deck' })).toEqual({ text: 'DECK' })
  })

  it('gets the error of a method that fails', async () => {
    const { left, right } = connectedPeers()
    right.handle('fail', () => {
      throw new RpcError(errorCodes.permissionDenied, 'No scope.')
    })

    await expect(left.call('fail', null)).rejects.toMatchObject({ code: errorCodes.permissionDenied, message: 'No scope.' })
  })

  it('gives the code "method not found" for an unknown method', async () => {
    const { left } = connectedPeers()

    await expect(left.call('echo', { text: 'a' })).rejects.toMatchObject({ code: errorCodes.methodNotFound })
  })

  it('stops a call at the end of the time limit', async () => {
    const { left, right } = connectedPeers({ timeoutMs: 30 })
    right.handle('never', () => new Promise<null>(() => undefined))

    await expect(left.call('never', null)).rejects.toMatchObject({ code: errorCodes.timeout })
  })

  it('uses the time limit of the call before the time limit of the peer', async () => {
    const { left, right } = connectedPeers({ timeoutMs: 5000 })
    right.handle('never', () => new Promise<null>(() => undefined))
    const start = Date.now()

    await expect(left.call('never', null, { timeoutMs: 20 })).rejects.toMatchObject({ code: errorCodes.timeout })
    expect(Date.now() - start).toBeLessThan(1000)
  })

  it('refuses to send a message that is larger than the size limit', async () => {
    const { left, right } = connectedPeers({ maxMessageBytes: 100 })
    right.handle('echo', ({ text }) => ({ text }))

    await expect(left.call('echo', { text: 'x'.repeat(200) })).rejects.toMatchObject({ code: errorCodes.messageTooLarge })
  })

  it('gives an error when the answer is larger than the size limit of the other side', async () => {
    const { left, right } = connectedPeers({ maxMessageBytes: 100 })
    right.handle('echo', ({ text }) => ({ text: text.repeat(50) }))

    await expect(left.call('echo', { text: 'abc' })).rejects.toMatchObject({ code: errorCodes.messageTooLarge })
  })

  it('answers a message that is not JSON with a parse error, and ignores messages that are not strings', async () => {
    const channel = new MessageChannel()
    const peer = new RpcPeer<Methods, Methods>(channel.port1 as unknown as PortLike)
    peers.push(peer)
    const answers: unknown[] = []
    channel.port2.onmessage = (event) => answers.push(JSON.parse(event.data as string))

    channel.port2.postMessage({ jsonrpc: '2.0', id: 1, method: 'echo' })
    channel.port2.postMessage('{not json')
    await new Promise((resolve) => setTimeout(resolve, 20))
    channel.port2.close()

    expect(answers).toEqual([{ jsonrpc: '2.0', id: null, error: { code: errorCodes.parseError, message: 'The message is not valid JSON.' } }])
  })

  it('rejects the waiting calls when the peer closes', async () => {
    const { left, right } = connectedPeers()
    right.handle('never', () => new Promise<null>(() => undefined))

    const call = left.call('never', null)
    left.close()

    await expect(call).rejects.toMatchObject({ code: errorCodes.connectionClosed })
    await expect(left.call('never', null)).rejects.toMatchObject({ code: errorCodes.connectionClosed })
  })
})

describe('utf8Length', () => {
  it('counts the bytes of UTF-8', () => {
    expect(utf8Length('abc')).toBe(3)
    expect(utf8Length('é')).toBe(2)
    expect(utf8Length('€')).toBe(3)
    expect(utf8Length('🃏')).toBe(4)
    expect(utf8Length('🃏é')).toBe(new TextEncoder().encode('🃏é').byteLength)
  })
})
