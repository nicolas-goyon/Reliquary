import { useCallback, useMemo, useReducer, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Deck } from '../../api/decks'
import { playerSetups, startingLife, usesCommanderDamage } from '../../playtest/deckSetup'
import { createTable, type TableAction, type TableState } from '../../playtest/engine'
import { gameReducer } from '../../playtest/game'
import { newGameId, newSeed } from '../../playtest/gameId'
import { projectPublicState } from '../../playtest/projection'
import { useGameHost } from '../../playtest/sync'
import type { LocalMode } from './LocalGameSetup'
import { PlayerCounters } from './PlayerCounters'
import { PlayerSummary } from './PlayerSummary'
import { PlayerTable } from './PlayerTable'
import { SharedScreenPanel } from './SharedScreenPanel'

/**
 * A local multiplayer game: one person plays 3 or 4 decks on one computer.
 * - Mode "turns": one window. The board of the active player shows, and the hand stays hidden until the person shows it.
 * - Mode "windows": this window keeps the game, and each player has a player window.
 * In the two modes, this window is the game window: the user can open a table window for the shared screen.
 * Each other window gets only the public projection of the game for its viewer.
 */
export function LocalGame({ decks, mode }: { decks: Deck[]; mode: LocalMode }) {
  const { t } = useTranslation('playtest')
  const setups = useMemo(() => playerSetups(decks), [decks])
  const life = startingLife(decks[0].format)
  const options = { commander: usesCommanderDamage(decks) }
  const [table, dispatch] = useReducer(gameReducer, undefined, () => createTable(setups, newSeed(), life, options))
  const [gameId] = useState(newGameId)
  const tableDispatch = useCallback((action: TableAction) => dispatch(action), [])
  useGameHost(gameId, table, tableDispatch, projectPublicState)

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
      {mode === 'turns' ? <TurnsBoard table={table} dispatch={tableDispatch} /> : <PlayerSummary table={table} />}
      <SharedScreenPanel gameId={gameId} players={mode === 'windows' ? table.players : []} />
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
