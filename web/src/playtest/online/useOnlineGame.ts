import { useEffect, useRef, useState } from 'react'
import type { TableAction } from '../engine'
import { applyViewChanges, viewHash, type PlayerView, type ViewChange } from '../views'
import type { Lobby, OnlineSnapshot } from './gameApi'
import { HubClient, type ActResult, type HubClientOptions, type HubStatus, type Patch } from './hubClient'

/** The state of an online game in the page. */
export interface OnlineGameState {
  status: HubStatus
  lobby: Lobby | null
  /** The view of the player. It is null before the start of the game. */
  view: PlayerView | null
  /** The public texts of the last actions, the newest first. */
  log: string[]
  /** The reason when the server closed the game: "ended", "kicked", "expired" or another reason. */
  closedReason: string | null
  /** The number of times that the view hash was different from the hash of the server. */
  desyncs: number
}

export interface OnlineGame extends OnlineGameState {
  act(action: TableAction): Promise<ActResult>
  invoke<T>(method: string, ...args: unknown[]): Promise<T>
}

/** The number of log lines that the page keeps. */
const logSize = 30

const initialState: OnlineGameState = { status: 'connecting', lobby: null, view: null, log: [], closedReason: null, desyncs: 0 }

/**
 * Gives the next state after a patch, and tells if the view is not in sync.
 * The view is not in sync when the patch cannot apply, or when the new view does not have the hash of the server.
 */
export function applyPatch(state: OnlineGameState, patch: Patch): { state: OnlineGameState; desync: boolean } {
  const log = patch.log ? [patch.log, ...state.log].slice(0, logSize) : state.log
  if (!state.view) {
    // In the lobby, each change of the lobby comes with an empty patch and an empty hash: there is no view yet.
    // A patch with a hash, for example the patch of the start, needs a full view.
    return { state: { ...state, log }, desync: patch.hash !== '' || patch.changes.length > 0 }
  }
  try {
    const view = applyViewChanges(state.view, patch.changes as ViewChange[])
    const desync = viewHash(view) !== patch.hash
    return { state: { ...state, view, log, desyncs: state.desyncs + (desync ? 1 : 0) }, desync }
  } catch {
    return { state: { ...state, log, desyncs: state.desyncs + 1 }, desync: true }
  }
}

/** Gives the next state after a snapshot. */
export function applySnapshot(state: OnlineGameState, snapshot: OnlineSnapshot): OnlineGameState {
  return { ...state, view: snapshot.view }
}

/**
 * Connects the page to the hub of an online game with the game token of one seat.
 * @param createSocket Makes the socket. The tests give a fake socket.
 */
export function useOnlineGame(gameToken: string | null, url: string, createSocket?: HubClientOptions['createSocket']): OnlineGame {
  const [state, setState] = useState<OnlineGameState>(initialState)
  const client = useRef<HubClient | null>(null)

  useEffect(() => {
    if (!gameToken) {
      return
    }
    // The state of the hub client is the source. The callbacks keep a copy for the page.
    let current = initialState
    const update = (next: OnlineGameState) => {
      current = next
      setState(next)
    }
    const hub = new HubClient({
      url,
      accessToken: () => gameToken,
      createSocket,
      onStatus: (status) => update({ ...current, status }),
      onLobby: (lobby) => update({ ...current, lobby: lobby as Lobby }),
      onSnapshot: (snapshot) => update(applySnapshot(current, snapshot as OnlineSnapshot)),
      onPatch: (patch) => {
        const result = applyPatch(current, patch)
        update(result.state)
        if (result.desync) {
          hub.resync()
        }
      },
      onClosed: (reason) => update({ ...current, status: 'closed', closedReason: reason }),
    })
    client.current = hub
    hub.start()
    return () => {
      hub.stop()
      client.current = null
    }
  }, [gameToken, url, createSocket])

  return {
    ...state,
    act: (action) => client.current?.act(action) ?? Promise.reject(new Error('The game is not connected.')),
    invoke: <T,>(method: string, ...args: unknown[]) => client.current?.invoke<T>(method, ...args) ?? Promise.reject(new Error('The game is not connected.')),
  }
}
