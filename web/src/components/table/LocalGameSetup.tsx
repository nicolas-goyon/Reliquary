import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { DeckSummary } from '../../api/decks'
import { localPlayers } from '../../playtest/deckSetup'

/** How one person plays the decks of a local multiplayer game. */
export type LocalMode = 'turns' | 'windows'

export interface LocalGameChoice {
  /** The IDs of the decks of the other players, in the order of play. */
  opponentIds: string[]
  mode: LocalMode
}

/** The form that starts a local multiplayer game: the number of players, the decks of the other players and the mode. */
export function LocalGameSetup({ deckId, deckName, decks, onStart }: { deckId: string; deckName: string; decks: DeckSummary[]; onStart: (choice: LocalGameChoice) => void }) {
  const { t } = useTranslation('playtest')
  const id = useId()
  const [count, setCount] = useState<number>(localPlayers.max)
  const [opponentIds, setOpponentIds] = useState<string[]>(() => Array.from({ length: localPlayers.max - 1 }, (_, index) => decks.filter((deck) => deck.id !== deckId)[index]?.id ?? deckId))
  const [mode, setMode] = useState<LocalMode>('turns')
  const options = [{ id: deckId, name: deckName }, ...decks.filter((deck) => deck.id !== deckId)]

  function submit(event: FormEvent) {
    event.preventDefault()
    onStart({ opponentIds: opponentIds.slice(0, count - 1), mode })
  }

  return (
    <form className="form panel" onSubmit={submit}>
      <h2>{t('local.setup')}</h2>
      <fieldset className="field">
        <legend>{t('local.playerCount')}</legend>
        {Array.from({ length: localPlayers.max - localPlayers.min + 1 }, (_, index) => localPlayers.min + index).map((value) => (
          <label key={value} className="table-choice">
            <input type="radio" name={`${id}-count`} value={value} checked={count === value} onChange={() => setCount(value)} />
            {t('local.playerCountValue', { count: value })}
          </label>
        ))}
      </fieldset>
      <p>{t('local.firstPlayer', { name: deckName })}</p>
      {opponentIds.slice(0, count - 1).map((opponentId, index) => (
        <div className="field" key={index}>
          <label htmlFor={`${id}-deck-${index}`}>{t('local.playerDeck', { number: index + 2 })}</label>
          <select
            id={`${id}-deck-${index}`}
            value={opponentId}
            onChange={(event) => setOpponentIds((current) => current.map((value, position) => (position === index ? event.target.value : value)))}
          >
            {options.map((option) => (
              <option key={option.id} value={option.id}>
                {option.name}
              </option>
            ))}
          </select>
        </div>
      ))}
      <fieldset className="field">
        <legend>{t('local.mode')}</legend>
        {(['turns', 'windows'] as const).map((value) => (
          <label key={value} className="table-choice">
            <input type="radio" name={`${id}-mode`} value={value} checked={mode === value} onChange={() => setMode(value)} />
            <span>
              {t(`local.modes.${value}`)}
              <span className="field-hint">{t(`local.modes.${value}Hint`)}</span>
            </span>
          </label>
        ))}
      </fieldset>
      <div className="form-actions">
        <button type="submit" className="button-primary">
          {t('local.start')}
        </button>
      </div>
    </form>
  )
}
