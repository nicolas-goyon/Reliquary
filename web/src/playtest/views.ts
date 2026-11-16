import { tableZones, type TableCard, type TablePlayer, type TableState, type TableZone } from './engine'
import { projectPublicState } from './projection'

/*
 * The view of one player in an online game, the view hash and the zone patches. ADR 0047 and issue #228 give the design.
 * The API makes the same views, hashes and patches in C#. The shared vectors in tests/vectors/views keep the two sides equal.
 */

/** The view of one player. It has the same contract as projectPublicState: the state that one viewer can see. */
export type PlayerView = TableState

/** Gives the view of one player, or the public view for the viewer ID "table". */
export function playerView(state: TableState, viewerId: string): PlayerView {
  return projectPublicState(state, viewerId)
}

/** A JSON value that canonicalJson accepts. */
export type JsonValue = null | boolean | number | string | JsonValue[] | { [key: string]: JsonValue | undefined }

function canonicalString(value: string): string {
  let result = '"'
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index)
    const char = value[index]
    if (char === '"' || char === '\\') {
      result += `\\${char}`
    } else if (code === 0x08) {
      result += '\\b'
    } else if (code === 0x09) {
      result += '\\t'
    } else if (code === 0x0a) {
      result += '\\n'
    } else if (code === 0x0c) {
      result += '\\f'
    } else if (code === 0x0d) {
      result += '\\r'
    } else if (code < 0x20) {
      result += `\\u${code.toString(16).padStart(4, '0')}`
    } else if (code >= 0xd800 && code <= 0xdbff && index + 1 < value.length && value.charCodeAt(index + 1) >= 0xdc00 && value.charCodeAt(index + 1) <= 0xdfff) {
      // A surrogate pair is one character: it goes to the output as it is.
      result += value.slice(index, index + 2)
      index++
    } else if (code >= 0xd800 && code <= 0xdfff) {
      // A lone surrogate cannot go to UTF-8.
      result += `\\u${code.toString(16)}`
    } else {
      result += char
    }
  }
  return `${result}"`
}

/**
 * Writes a value as canonical JSON. The C# side must give the same text, byte for byte.
 *
 * - No whitespace between the tokens.
 * - Object: the keys in ascending order of their UTF-16 code units (C#: `string.CompareOrdinal`). A key with the value `undefined` is not written.
 * - Array: the items in their order. An item must not be `undefined`.
 * - `null`, `true` and `false` as in JSON.
 * - Number: only safe integers (-(2^53 - 1) to 2^53 - 1). The text is the decimal form with no leading zeros, no plus sign, no exponent and no decimal point.
 *   A negative number starts with "-". Minus zero is "0". Other numbers make an error. This is the output of JavaScript `String(n)` for these values.
 * - String: a quotation mark, the characters, then a quotation mark. The function escapes these characters:
 *   - `"` as `\"` and `\` as `\\`
 *   - U+0008 as `\b`, U+0009 as `\t`, U+000A as `\n`, U+000C as `\f`, U+000D as `\r`
 *   - the other characters below U+0020 as `\u` and 4 lowercase hexadecimal digits, for example `\u001f`
 *   - a lone surrogate (U+D800 to U+DFFF with no pair) as `\u` and 4 lowercase hexadecimal digits, for example `\udc00`
 *   All the other characters stay as they are: no escape for `/`, for non-ASCII characters or for U+2028 and U+2029.
 *   This is the output of JavaScript `JSON.stringify` for strings.
 * - The bytes of the text are its UTF-8 encoding.
 */
export function canonicalJson(value: JsonValue): string {
  if (value === null) {
    return 'null'
  }
  switch (typeof value) {
    case 'boolean':
      return value ? 'true' : 'false'
    case 'number':
      if (!Number.isSafeInteger(value)) {
        throw new Error(`Canonical JSON accepts only safe integers. The value is ${value}.`)
      }
      return String(value)
    case 'string':
      return canonicalString(value)
    case 'object':
      if (Array.isArray(value)) {
        return `[${value
          .map((item) => {
            if (item === undefined) {
              throw new Error('Canonical JSON does not accept undefined in an array.')
            }
            return canonicalJson(item)
          })
          .join(',')}]`
      }
      return `{${Object.keys(value)
        .filter((key) => value[key] !== undefined)
        .sort()
        .map((key) => `${canonicalString(key)}:${canonicalJson(value[key] as JsonValue)}`)
        .join(',')}}`
    default:
      throw new Error(`Canonical JSON does not accept the type ${typeof value}.`)
  }
}

const fnvOffset = 0xcbf29ce484222325n
const fnvPrime = 0x100000001b3n

/**
 * The 64-bit FNV-1a hash of the UTF-8 bytes of a text, as 16 lowercase hexadecimal digits.
 * The start value is 0xcbf29ce484222325 and the prime is 0x100000001b3. For each byte: hash = (hash XOR byte) * prime, modulo 2^64.
 * The hash finds bugs. It is not a security function.
 */
export function fnv1a64(text: string): string {
  let hash = fnvOffset
  for (const byte of new TextEncoder().encode(text)) {
    hash = BigInt.asUintN(64, (hash ^ BigInt(byte)) * fnvPrime)
  }
  return hash.toString(16).padStart(16, '0')
}

/** The hash of a view: the 64-bit FNV-1a hash of its canonical JSON. */
export function viewHash(view: PlayerView): string {
  return fnv1a64(canonicalJson(view as unknown as JsonValue))
}

/** The fields of a player that a patch can change. The zones have their own change. */
export const playerFields = ['name', 'life', 'commanderDamage', 'counters', 'mulligans', 'cardsToBottom'] as const
export type PlayerField = (typeof playerFields)[number]

/** The fields of the table that a patch can change. The players have their own changes. */
export const tableFields = ['activePlayerId', 'turn', 'commander', 'seed', 'nextTokenId'] as const
export type TableField = (typeof tableFields)[number]

/**
 * One change of a patch:
 * - `{ seat, zone, cards }`: the full new content of one zone of one player in this view.
 * - `{ seat, field, value }`: the new value of one field of one player.
 * - `{ field, value }`: the new value of one field of the table.
 * The seat is the ID of the player.
 */
export type ViewChange =
  | { seat: string; zone: TableZone; cards: TableCard[] }
  | { seat: string; field: PlayerField; value: TablePlayer[PlayerField] }
  | { field: TableField; value: TableState[TableField] }

const same = (left: unknown, right: unknown) => canonicalJson(left as JsonValue) === canonicalJson(right as JsonValue)

/**
 * Gives the changes from one view to the next view of the same viewer.
 * The changes come in a fixed order: the table fields, then each player in the order of the view, with its fields and then its zones.
 * The two views must have the same players in the same order. Send a full view in the other case.
 */
export function diffViews(before: PlayerView, after: PlayerView): ViewChange[] {
  if (before.players.length !== after.players.length || before.players.some((player, index) => player.id !== after.players[index].id)) {
    throw new Error('The two views do not have the same players. Send a full view.')
  }
  const changes: ViewChange[] = []
  for (const field of tableFields) {
    if (!same(before[field], after[field])) {
      changes.push({ field, value: after[field] } as ViewChange)
    }
  }
  after.players.forEach((player, index) => {
    const old = before.players[index]
    for (const field of playerFields) {
      if (!same(old[field], player[field])) {
        changes.push({ seat: player.id, field, value: player[field] } as ViewChange)
      }
    }
    for (const zone of tableZones) {
      if (!same(old.zones[zone], player.zones[zone])) {
        changes.push({ seat: player.id, zone, cards: player.zones[zone] })
      }
    }
  })
  return changes
}

/**
 * Applies the changes of a patch to a view. It is a pure function: the view does not change.
 * A change for a seat that is not in the view makes an error, because the view is then not in sync. Ask for a full view.
 */
export function applyViewChanges(view: PlayerView, changes: readonly ViewChange[]): PlayerView {
  let next: PlayerView = { ...view, players: [...view.players] }
  for (const change of changes) {
    if (!('seat' in change)) {
      next = { ...next, [change.field]: change.value }
      continue
    }
    const index = next.players.findIndex((player) => player.id === change.seat)
    if (index < 0) {
      throw new Error(`The view has no player "${change.seat}". Ask for a full view.`)
    }
    const player = next.players[index]
    next.players[index] = 'zone' in change ? { ...player, zones: { ...player.zones, [change.zone]: change.cards } } : { ...player, [change.field]: change.value }
  }
  return next
}
