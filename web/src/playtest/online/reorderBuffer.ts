/** A message with a sequence number. The server gives each change of a game the next number. */
export interface Sequenced {
  seq: number
}

/**
 * Puts the patches of a game in the order of their sequence numbers.
 * The Redis backplane can deliver the patches of two API replicas in a different order, so a patch can arrive early.
 * The buffer holds an early patch until the patches before it arrive. It is a pure data structure: the caller gives the time.
 */
export class ReorderBuffer<T extends Sequenced> {
  private held = new Map<number, T>()
  private gapSince: number | null = null
  private lastSeq: number

  /** @param lastSeq The sequence number of the last change in the view of the client, for example the number of a snapshot. */
  constructor(lastSeq: number) {
    this.lastSeq = lastSeq
  }

  /** The sequence number of the last patch that the buffer gave. */
  get seq(): number {
    return this.lastSeq
  }

  /** The number of patches that wait for an earlier patch. */
  get waiting(): number {
    return this.held.size
  }

  /**
   * Adds a patch. It gives the patches that the client can apply now, in order.
   * The buffer ignores a patch that it gave before or that it holds.
   * @param now The current time in milliseconds.
   */
  push(patch: T, now: number): T[] {
    if (patch.seq > this.lastSeq && !this.held.has(patch.seq)) {
      this.held.set(patch.seq, patch)
    }
    const ready: T[] = []
    for (let next = this.held.get(this.lastSeq + 1); next; next = this.held.get(this.lastSeq + 1)) {
      this.held.delete(next.seq)
      this.lastSeq = next.seq
      ready.push(next)
    }
    if (this.held.size === 0) {
      this.gapSince = null
    } else if (ready.length > 0 || this.gapSince === null) {
      // A new gap starts: the patch after the last patch is missing.
      this.gapSince = now
    }
    return ready
  }

  /** The time in milliseconds since the start of the current gap, or null when there is no gap. */
  gapAge(now: number): number | null {
    return this.gapSince === null ? null : now - this.gapSince
  }

  /**
   * Starts again from a snapshot. The buffer removes the patches that the snapshot contains.
   * It gives the held patches that come directly after the snapshot, in order.
   */
  reset(seq: number, now: number): T[] {
    this.lastSeq = seq
    for (const key of [...this.held.keys()]) {
      if (key <= seq) {
        this.held.delete(key)
      }
    }
    this.gapSince = null
    const held = [...this.held.values()]
    this.held.clear()
    return held.flatMap((patch) => this.push(patch, now))
  }
}
