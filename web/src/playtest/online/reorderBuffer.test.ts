import { describe, expect, it } from 'vitest'
import { ReorderBuffer } from './reorderBuffer'

const patch = (seq: number) => ({ seq })
const seqs = (items: { seq: number }[]) => items.map((item) => item.seq)

describe('reorder buffer', () => {
  it('gives the patches in order, and holds an early patch until the gap closes', () => {
    const buffer = new ReorderBuffer(10)
    expect(seqs(buffer.push(patch(11), 0))).toEqual([11])
    expect(seqs(buffer.push(patch(13), 5))).toEqual([])
    expect(buffer.waiting).toBe(1)
    expect(buffer.gapAge(305)).toBe(300)
    expect(seqs(buffer.push(patch(14), 400))).toEqual([])
    expect(buffer.gapAge(405)).toBe(400)
    expect(seqs(buffer.push(patch(12), 500))).toEqual([12, 13, 14])
    expect(buffer.seq).toBe(14)
    expect(buffer.gapAge(600)).toBeNull()
  })

  it('ignores a patch that it gave or holds', () => {
    const buffer = new ReorderBuffer(1)
    buffer.push(patch(2), 0)
    buffer.push(patch(4), 0)
    expect(buffer.push(patch(2), 0)).toEqual([])
    expect(buffer.push(patch(4), 0)).toEqual([])
    expect(buffer.waiting).toBe(1)
  })

  it('starts a new gap when a part of the held patches becomes ready', () => {
    const buffer = new ReorderBuffer(0)
    buffer.push(patch(2), 0)
    buffer.push(patch(4), 0)
    expect(seqs(buffer.push(patch(1), 800))).toEqual([1, 2])
    expect(buffer.gapAge(900)).toBe(100)
  })

  it('starts again from a snapshot and gives the held patches after it', () => {
    const buffer = new ReorderBuffer(0)
    buffer.push(patch(3), 0)
    buffer.push(patch(6), 0)
    buffer.push(patch(7), 0)
    expect(seqs(buffer.reset(5, 10))).toEqual([6, 7])
    expect(buffer.seq).toBe(7)
    expect(buffer.waiting).toBe(0)
    expect(buffer.gapAge(20)).toBeNull()
  })
})
