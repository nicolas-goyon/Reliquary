import { useCallback, useMemo, useReducer, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Deck } from '../../api/decks'
import { playerSetups, startingLife, usesCommanderDamage } from '../../playtest/deckSetup'
import { createTable, type TableAction, type TableState } from '../../playtest/engine'
import { gameReducer, newSeed } from '../../playtest/game'
import { newGameId, playerWindowPath } from '../../playtest/gameId'
import { useGameHost } from '../../playtest/sync'
import type { LocalMode } from './LocalGameSetup'
import { PlayerCounters } from './PlayerCounters'
import { PlayerSummary } from './PlayerSummary'
import { PlayerTable } from './PlayerTable'

/** In local multiplayer, each player window gets the full state. A player window shows only the hand of its own player. */
const fullState = (state: TableState) => state

/**
 * A local multiplayer game: one person plays 3 or 4 decks on one computer.
 * - Mode "turns": one window. The board of the active player shows, and the hand stays hidden until the person shows it.
 * - Mode "windows": this window keeps the game, and each player has a player window.
 */
export function LocalGame({ decks, mode }: { decks: Deck[]; mode: LocalMode }) {
  const { t } = useTranslation('playtest')
  const setups = useMemo(() => playerSetups(decks), [decks])
  const life = startingLife(decks[0].format)
  const options = { commander: usesCommanderDamage(decks) }
  const [table, dispatch] = useReducer(gameReducer, undefined, () => createTable(setups, newSeed(), life, options))
  const [gameId] = useState(newGameId)
  const tableDispatch = useCallback((action: TableAction) => dispatch(action), [])
  useGameHost(mode === 'windows' ? gameId : null, table, tableDispatch, fullState)

  function restart() {
    dispatch({ type: 'restart', table: createTable(setups, newSeed(), life, options) })
  }

  return (
    <>
      <div className="form-actions">
        <strong>{t('turn', { turn: table.turn })}</strong>
        <button type="button" className="button-primary" onClick={() => dispatch({ type: 'nextTurn' })}>
          {t('local.nextPlayer')}
        </button>
        <button type="button" onClick={restart}>
          {t('controls.restart')}
        </button>
      </div>
      {mode === 'turns' ? <TurnsBoard table={table} dispatch={tableDispatch} /> : <WindowsBoard table={table} gameId={gameId} />}
    </>
  )
}

/** Mode "turns": the board of one player in the window. The hand stays hidden until the person shows it. */
function TurnsBoard({ table, dispatch }: { table: TableState; dispatch: (action: TableAction) => void }) {
  const { t } = useTranslation('playtest')
  const [selected, setSelected] = useState({ id: table.activePlayerId, turn: table.turn })
  const [shownHand, setShownHand] = useState<{ id: string; turn: number } | null>(null)
  // A new turn shows the board of the new active player.
  const selectedId = selected.turn === table.turn ? selected.id : table.activePlayerId
  const player = table.players.find((item) => item.id === selectedId) ?? table.players[0]
  const handShown = shownHand?.id === player.id && shownHand.turn === table.turn

  return (
    <>
      <PlayerSummary table={table} selectedId={player.id} onSelect={(item) => setSelected({ id: item.id, turn: table.turn })} />
      <h2 className="table-board-title">{t('local.board', { name: player.name })}</h2>
      <PlayerTable
        key={player.id}
        player={player}
        dispatch={dispatch}
        hiddenHand={
          handShown ? undefined : (
            <>
              <p className="field-hint">{t('local.handHidden', { name: player.name })}</p>
              <button type="button" className="button-primary" onClick={() => setShownHand({ id: player.id, turn: table.turn })}>
                {t('local.showHand', { name: player.name })}
              </button>
            </>
          )
        }
        side={<PlayerCounters player={player} others={table.players.filter((other) => other.id !== player.id)} commander={table.commander} dispatch={dispatch} />}
      />
      {handShown && (
        <div className="form-actions">
          <button type="button" onClick={() => setShownHand(null)}>
            {t('local.hideHand', { name: player.name })}
          </button>
        </div>
      )}
    </>
  )
}

/** Mode "windows": this window keeps the game and gives a link to the window of each player. */
function WindowsBoard({ table, gameId }: { table: TableState; gameId: string }) {
  const { t } = useTranslation('playtest')
  return (
    <>
      <PlayerSummary table={table} />
      <section className="panel">
        <h2>{t('local.windowsTitle')}</h2>
        <p>{t('local.windowsHint')}</p>
        <ul className="table-window-links">
          {table.players.map((player) => (
            <li key={player.id}>
              <a href={playerWindowPath(gameId, player.id)} target="_blank" rel="noopener">
                {t('local.openPlayerWindow', { name: player.name })}
              </a>
            </li>
          ))}
        </ul>
      </section>
    </>
  )
}
