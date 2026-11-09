import { useId, useState, type Dispatch, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { tableZones, type TableAction, type TableCard, type TablePlayer, type TableZone } from '../../playtest/engine'
import { CardImage } from '../CardImage'

/** One card on the table, with its actions. */
function CardControl({ card, zone, player, dispatch }: { card: TableCard; zone: TableZone; player: TablePlayer; dispatch: Dispatch<TableAction> }) {
  const { t } = useTranslation('playtest')
  const id = useId()
  const playerId = player.id
  const name = card.name
  const mustBottom = zone === 'hand' && player.cardsToBottom > 0

  function mainAction() {
    if (mustBottom) {
      dispatch({ type: 'bottom', playerId, cardId: card.id })
    } else if (zone === 'battlefield') {
      dispatch({ type: 'tap', playerId, cardId: card.id })
    } else {
      dispatch({ type: 'move', playerId, cardId: card.id, to: 'battlefield' })
    }
  }

  const label = mustBottom ? t('card.bottom', { name }) : zone === 'battlefield' ? t('card.tap', { name }) : t('card.play', { name })
  return (
    <li className={card.tapped ? 'table-card table-card-tapped' : 'table-card'}>
      <button type="button" className="table-card-face" aria-label={label} aria-pressed={zone === 'battlefield' ? card.tapped : undefined} onClick={mainAction}>
        {card.faceDown ? <span className="table-card-back">{t('card.back')}</span> : <CardImage src={card.imageUri ?? undefined} alt={name} />}
      </button>
      {card.counters > 0 && <span className="badge">{t('card.counters', { count: card.counters })}</span>}
      <div className="table-card-actions">
        <label className="visually-hidden" htmlFor={id}>
          {t('card.moveTo', { name })}
        </label>
        <select
          id={id}
          value=""
          onChange={(event) => {
            if (event.target.value) {
              dispatch({ type: 'move', playerId, cardId: card.id, to: event.target.value as TableZone })
            }
          }}
        >
          <option value="">{t('card.move')}</option>
          {tableZones
            .filter((target) => target !== zone)
            .map((target) => (
              <option key={target} value={target}>
                {t(`zones.${target}`)}
              </option>
            ))}
        </select>
        {zone === 'battlefield' && (
          <>
            <button type="button" className="button-small" aria-label={t('card.flip', { name })} onClick={() => dispatch({ type: 'flip', playerId, cardId: card.id })}>
              ⟲
            </button>
            <button type="button" className="button-small" aria-label={t('card.addCounter', { name })} onClick={() => dispatch({ type: 'counter', playerId, cardId: card.id, change: 1 })}>
              +
            </button>
            <button type="button" className="button-small" aria-label={t('card.removeCounter', { name })} onClick={() => dispatch({ type: 'counter', playerId, cardId: card.id, change: -1 })}>
              −
            </button>
          </>
        )}
      </div>
    </li>
  )
}

/** A zone of a player. When hidden is set, the zone shows only the number of cards and the hidden content. */
function CardZone({ zone, player, dispatch, hidden }: { zone: TableZone; player: TablePlayer; dispatch: Dispatch<TableAction>; hidden?: ReactNode }) {
  const { t } = useTranslation('playtest')
  const cards = player.zones[zone]
  if (hidden !== undefined) {
    return (
      <section className="table-zone table-zone-hidden" aria-label={t(`zones.${zone}`)}>
        <h2>
          {t(`zones.${zone}`)} ({cards.length})
        </h2>
        {hidden}
      </section>
    )
  }
  return (
    <section className="table-zone" aria-label={t(`zones.${zone}`)}>
      <h2>
        {t(`zones.${zone}`)} ({cards.length})
      </h2>
      <ul className="table-cards">
        {cards.map((card) => (
          <CardControl key={card.id} card={card} zone={zone} player={player} dispatch={dispatch} />
        ))}
      </ul>
    </section>
  )
}

interface PlayerTableProps {
  player: TablePlayer
  dispatch: Dispatch<TableAction>
  /**
   * Hides the cards of the hand: the hand zone shows only the number of cards and this content.
   * Local multiplayer hides the hand until the player shows it.
   */
  hiddenHand?: ReactNode
  /** An element at the top of the side column, for example the counters of the player. */
  side?: ReactNode
}

/** The zones and the controls of one player. */
export function PlayerTable({ player, dispatch, hiddenHand, side }: PlayerTableProps) {
  const { t } = useTranslation('playtest')
  const [tokenName, setTokenName] = useState('')
  const playerId = player.id
  const tokenField = useId()

  function createToken(event: FormEvent) {
    event.preventDefault()
    if (tokenName.trim()) {
      dispatch({ type: 'token', playerId, name: tokenName.trim() })
      setTokenName('')
    }
  }

  return (
    <>
      <div className="table-status">
        <span className="table-life">
          <button type="button" className="button-small" aria-label={t('life.decrease')} onClick={() => dispatch({ type: 'life', playerId, change: -1 })}>
            −
          </button>
          <span aria-label={t('life.label')}>{player.life}</span>
          <button type="button" className="button-small" aria-label={t('life.increase')} onClick={() => dispatch({ type: 'life', playerId, change: 1 })}>
            +
          </button>
        </span>
        <div className="form-actions" role="toolbar" aria-label={t('controls.label')}>
          <button type="button" className="button-primary" onClick={() => dispatch({ type: 'draw', playerId })}>
            {t('controls.draw')} ({player.zones.library.length})
          </button>
          <button type="button" onClick={() => dispatch({ type: 'mulligan', playerId })}>
            {t('controls.mulligan')}
          </button>
          <button type="button" onClick={() => dispatch({ type: 'shuffle', playerId })}>
            {t('controls.shuffle')}
          </button>
          <button type="button" onClick={() => dispatch({ type: 'untapAll', playerId })}>
            {t('controls.untapAll')}
          </button>
        </div>
      </div>
      {player.cardsToBottom > 0 && hiddenHand === undefined && (
        <p className="alert alert-warning" role="status">
          {t('mulligan.bottom', { count: player.cardsToBottom })}
        </p>
      )}
      <div className="table-layout">
        <div>
          <CardZone zone="battlefield" player={player} dispatch={dispatch} />
          <CardZone zone="hand" player={player} dispatch={dispatch} hidden={hiddenHand} />
        </div>
        <div className="table-side">
          {side}
          <form className="form table-zone" onSubmit={createToken}>
            <div className="field">
              <label htmlFor={tokenField}>{t('token.name')}</label>
              <input id={tokenField} value={tokenName} maxLength={60} onChange={(event) => setTokenName(event.target.value)} />
            </div>
            <button type="submit">{t('token.create')}</button>
          </form>
          <CardZone zone="command" player={player} dispatch={dispatch} />
          <CardZone zone="graveyard" player={player} dispatch={dispatch} />
          <CardZone zone="exile" player={player} dispatch={dispatch} />
        </div>
      </div>
    </>
  )
}
