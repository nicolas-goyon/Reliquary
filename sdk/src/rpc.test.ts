import assert from 'node:assert/strict'
import { afterEach, describe, it } from 'node:test'
import { errorCodes } from './protocol.ts'
import { RpcError, RpcPeer, utf8Length, type PortLike } from './rpc.ts'

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

    assert.deepEqual(await left.call('echo', { text: 'deck' }), { text: 'DECK' })
  })

  it('gets the error of a method that fails', async () => {
    const { left, right } = connectedPeers()
    right.handle('fail', () => {
      throw new RpcError(errorCodes.permissionDenied, 'No scope.')
    })

    await assert.rejects(left.call('fail', null), (error: RpcError) => error.code === errorCodes.permissionDenied && error.message === 'No scope.')
  })

  it('gives the code "method not found" for an unknown method', async () => {
    const { left } = connectedPeers()

    await assert.rejects(left.call('echo', { text: 'a' }), (error: RpcError) => error.code === errorCodes.methodNotFound)
  })

  it('stops a call at the end of the time limit', async () => {
    const { left, right } = connectedPeers({ timeoutMs: 30 })
    right.handle('never', () => new Promise<null>(() => undefined))

    await assert.rejects(left.call('never', null), (error: RpcError) => error.code === errorCodes.timeout)
  })

  it('uses the time limit of the call before the time limit of the peer', async () => {
    const { left, right } = connectedPeers({ timeoutMs: 5000 })
    right.handle('never', () => new Promise<null>(() => undefined))
    const start = Date.now()

    await assert.rejects(left.call('never', null, { timeoutMs: 20 }), (error: RpcError) => error.code === errorCodes.timeout)
    assert.ok(Date.now() - start < 1000)
  })

  it('refuses to send a message that is larger than the size limit', async () => {
    const { left, right } = connectedPeers({ maxMessageBytes: 100 })
    right.handle('echo', ({ text }) => ({ text }))

    await assert.rejects(left.call('echo', { text: 'x'.repeat(200) }), (error: RpcError) => error.code === errorCodes.messageTooLarge)
  })

  it('gives an error when the answer is larger than the size limit of the other side', async () => {
    const { left, right } = connectedPeers({ maxMessageBytes: 100 })
    right.handle('echo', ({ text }) => ({ text: text.repeat(50) }))

    await assert.rejects(left.call('echo', { text: 'abc' }), (error: RpcError) => error.code === errorCodes.messageTooLarge)
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

    assert.deepEqual(answers, [{ jsonrpc: '2.0', id: null, error: { code: errorCodes.parseError, message: 'The message is not valid JSON.' } }])
  })

  it('rejects the waiting calls when the peer closes', async () => {
    const { left, right } = connectedPeers()
    right.handle('never', () => new Promise<null>(() => undefined))

    const call = left.call('never', null)
    left.close()

    await assert.rejects(call, (error: RpcError) => error.code === errorCodes.connectionClosed)
    await assert.rejects(left.call('never', null), (error: RpcError) => error.code === errorCodes.connectionClosed)
  })
})

describe('utf8Length', () => {
  it('counts the bytes of UTF-8', () => {
    assert.equal(utf8Length('abc'), 3)
    assert.equal(utf8Length('é'), 2)
    assert.equal(utf8Length('€'), 3)
    assert.equal(utf8Length('🃏'), 4)
    assert.equal(utf8Length('🃏é'), new TextEncoder().encode('🃏é').byteLength)
  })
})
