import { useEffect, useRef, useState, type Dispatch } from 'react'
import { playerCounters, tableZones, type TableAction, type TableState } from './engine'

/*
 * The window sync of a game on one computer. ADR 0045 gives the reasons.
 * - The game window keeps the table state. It is the only window that applies the actions.
 * - Each other window is a viewer. A viewer sends its actions to the game window, and it shows the state that the game window sends to it.
 * - The windows use BroadcastChannel, so the data stays in the browser.
 * - The viewers send their messages on the control channel of the game. The game window sends the state of each viewer on the channel of that viewer only.
 *   A window gets only the data of its viewer.
 */

/** A message from a viewer, or the message "ready" from the game window, on the control channel. */
export type ControlMessage =
  | { type: 'hello'; gameId: string; viewerId: string }
  | { type: 'action'; gameId: string; viewerId: string; action: TableAction }
  /** The game window opened: the viewers send "hello" again. */
  | { type: 'ready'; gameId: string }
  /** The game window closed. */
  | { type: 'closed'; gameId: string }

/** The state that the game window sends to one viewer. */
export interface ViewMessage {
  type: 'state'
  gameId: string
  state: TableState
}

/** The viewer ID of the table window. Each player window uses the ID of its player. */
export const tableViewerId = 'table'

export function controlChannelName(gameId: string): string {
  return `reliquary.game.${gameId}`
}

export function viewChannelName(gameId: string, viewerId: string): string {
  return `reliquary.game.${gameId}.view.${viewerId}`
}

type FieldKind = 'string' | 'number' | 'optionalCount' | 'zone' | 'counter' | 'position'

/** The fields of each table action. TypeScript makes sure that the list has all the action types. */
const actionFields: Record<TableAction['type'], Record<string, FieldKind>> = {
  draw: { playerId: 'string', count: 'optionalCount' },
  shuffle: { playerId: 'string' },
  mulligan: { playerId: 'string' },
  bottom: { playerId: 'string', cardId: 'string' },
  move: { playerId: 'string', cardId: 'string', to: 'zone', position: 'position' },
  tap: { playerId: 'string', cardId: 'string' },
  untapAll: { playerId: 'string' },
  flip: { playerId: 'string', cardId: 'string' },
  counter: { playerId: 'string', cardId: 'string', change: 'number' },
  token: { playerId: 'string', name: 'string' },
  life: { playerId: 'string', change: 'number' },
  commanderDamage: { playerId: 'string', fromPlayerId: 'string', change: 'number' },
  playerCounter: { playerId: 'string', counter: 'counter', change: 'number' },
  nextTurn: {},
}

function isField(value: unknown, kind: FieldKind): boolean {
  switch (kind) {
    case 'string':
      return typeof value === 'string'
    case 'number':
      return Number.isInteger(value)
    case 'optionalCount':
      return value === undefined || (Number.isInteger(value) && (value as number) >= 0)
    case 'zone':
      return (tableZones as readonly unknown[]).includes(value)
    case 'counter':
      return (playerCounters as readonly unknown[]).includes(value)
    case 'position':
      return value === undefined || value === 'top' || value === 'bottom'
  }
}

/** Checks that a value from another window is a table action with the correct fields. */
export function isTableAction(value: unknown): value is TableAction {
  if (!value || typeof value !== 'object') {
    return false
  }
  const record = value as Record<string, unknown>
  const type = record.type
  if (typeof type !== 'string' || !Object.hasOwn(actionFields, type)) {
    return false
  }
  return Object.entries(actionFields[type as TableAction['type']]).every(([name, kind]) => isField(record[name], kind))
}

/**
 * Tells if a viewer can do an action. A player window acts only for its own player.
 * Any player can give the turn to the next player. The table window cannot do actions.
 */
export function canViewerAct(viewerId: string, action: TableAction): boolean {
  return action.type === 'nextTurn' ? viewerId !== tableViewerId : action.playerId === viewerId
}

/** Reads a message of the control channel. It gives null for a message of another game or for a message with a wrong format. */
export function parseControlMessage(data: unknown, gameId: string): ControlMessage | null {
  if (!data || typeof data !== 'object') {
    return null
  }
  const message = data as Record<string, unknown>
  if (message.gameId !== gameId) {
    return null
  }
  switch (message.type) {
    case 'ready':
    case 'closed':
      return { type: message.type, gameId }
    case 'hello':
      return typeof message.viewerId === 'string' ? { type: 'hello', gameId, viewerId: message.viewerId } : null
    case 'action':
      return typeof message.viewerId === 'string' && isTableAction(message.action) ? { type: 'action', gameId, viewerId: message.viewerId, action: message.action } : null
    default:
      return null
  }
}

function parseViewMessage(data: unknown, gameId: string): ViewMessage | null {
  if (!data || typeof data !== 'object') {
    return null
  }
  const message = data as Record<string, unknown>
  const state = message.state as TableState | undefined
  return message.type === 'state' && message.gameId === gameId && state && Array.isArray(state.players) ? { type: 'state', gameId, state } : null
}

/** Gives the state that one viewer can see. */
export type ViewFunction = (state: TableState, viewerId: string) => TableState

function openChannel(name: string): BroadcastChannel | null {
  return typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(name)
}

/**
 * Makes the current window the game window of a game.
 * It sends to each viewer the result of view, and it applies the actions that the viewers can do.
 * Give null as the game ID when the game has only one window.
 */
export function useGameHost(gameId: string | null, state: TableState, dispatch: Dispatch<TableAction>, view: ViewFunction): void {
  const viewers = useRef(new Map<string, BroadcastChannel>())
  const current = useRef({ state, view })
  useEffect(() => {
    current.current = { state, view }
  }, [state, view])

  useEffect(() => {
    const control = gameId ? openChannel(controlChannelName(gameId)) : null
    if (!gameId || !control) {
      return
    }
    const channels = viewers.current
    const send = (viewerId: string) => {
      channels.get(viewerId)?.postMessage({ type: 'state', gameId, state: current.current.view(current.current.state, viewerId) } satisfies ViewMessage)
    }
    control.onmessage = (event: MessageEvent) => {
      const message = parseControlMessage(event.data, gameId)
      if (message?.type === 'hello') {
        if (!channels.has(message.viewerId)) {
          const channel = openChannel(viewChannelName(gameId, message.viewerId))
          if (channel) {
            channels.set(message.viewerId, channel)
          }
        }
        send(message.viewerId)
      } else if (message?.type === 'action' && canViewerAct(message.viewerId, message.action)) {
        dispatch(message.action)
      }
    }
    control.postMessage({ type: 'ready', gameId } satisfies ControlMessage)
    return () => {
      control.postMessage({ type: 'closed', gameId } satisfies ControlMessage)
      control.close()
      for (const channel of channels.values()) {
        channel.close()
      }
      channels.clear()
    }
  }, [gameId, dispatch])

  useEffect(() => {
    if (!gameId) {
      return
    }
    for (const [viewerId, channel] of viewers.current) {
      channel.postMessage({ type: 'state', gameId, state: view(state, viewerId) } satisfies ViewMessage)
    }
  }, [gameId, state, view])
}

export type GameViewerStatus = 'waiting' | 'connected' | 'closed' | 'unsupported'

export interface GameViewer {
  status: GameViewerStatus
  /** The state that the game window sent to this viewer. */
  state: TableState | null
  /** Sends an action to the game window. */
  send(action: TableAction): void
}

/** Connects a window to the game window of a game, as one viewer. */
export function useGameViewer(gameId: string, viewerId: string): GameViewer {
  const [state, setState] = useState<TableState | null>(null)
  const [status, setStatus] = useState<GameViewerStatus>(() => (typeof BroadcastChannel === 'undefined' ? 'unsupported' : 'waiting'))
  const control = useRef<BroadcastChannel | null>(null)

  useEffect(() => {
    const controlChannel = openChannel(controlChannelName(gameId))
    const viewChannel = openChannel(viewChannelName(gameId, viewerId))
    control.current = controlChannel
    if (!controlChannel || !viewChannel) {
      return
    }
    const hello = () => controlChannel.postMessage({ type: 'hello', gameId, viewerId } satisfies ControlMessage)
    controlChannel.onmessage = (event: MessageEvent) => {
      const message = parseControlMessage(event.data, gameId)
      if (message?.type === 'ready') {
        hello()
      } else if (message?.type === 'closed') {
        setStatus('closed')
      }
    }
    viewChannel.onmessage = (event: MessageEvent) => {
      const message = parseViewMessage(event.data, gameId)
      if (message) {
        setState(message.state)
        setStatus('connected')
      }
    }
    hello()
    return () => {
      controlChannel.close()
      viewChannel.close()
      control.current = null
    }
  }, [gameId, viewerId])

  return {
    status,
    state,
    send: (action) => control.current?.postMessage({ type: 'action', gameId, viewerId, action } satisfies ControlMessage),
  }
}
