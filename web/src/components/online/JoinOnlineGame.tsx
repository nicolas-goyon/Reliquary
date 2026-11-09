import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../api/client'
import type { DeckTextLineError } from '../../api/decks'
import { useAuth } from '../../auth/session'
import { deckIdOf, joinGame, joinLimits, storeSeat, type JoinDeck, type SeatAccess } from '../../playtest/online/gameApi'
import { ErrorAlert, TextField } from '../Forms'
import { LineErrorList } from '../LineErrors'

type DeckSource = 'deck' | 'text'

/**
 * The form that joins an online game with the invite link.
 * A user with an account gives a deck. A guest also gives a nickname. The deck is a deck link (own deck or public deck) or a deck list.
 */
export function JoinOnlineGame({ gameId, inviteSecret, onJoined }: { gameId: string; inviteSecret: string; onJoined: (access: SeatAccess) => void }) {
  const { t } = useTranslation('playtest')
  const { state, request } = useAuth()
  const id = useId()
  const account = state.status === 'authenticated' ? state.account : null
  const guest = account === null
  // An account can change its name in the game. The default is its display name: the field shows it until the user types.
  const [typedNickname, setNickname] = useState<string | null>(null)
  const nickname = typedNickname ?? account?.displayName ?? ''
  const [source, setSource] = useState<DeckSource>('deck')
  const [deckLink, setDeckLink] = useState('')
  const [deckText, setDeckText] = useState('')
  const [working, setWorking] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [lineErrors, setLineErrors] = useState<DeckTextLineError[]>([])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setProblem(null)
    setLineErrors([])
    let deck: JoinDeck
    if (source === 'deck') {
      const deckId = deckIdOf(deckLink)
      if (!deckId) {
        setProblem(t('online.join.deckLinkInvalid'))
        return
      }
      deck = { deckId }
    } else {
      if (!deckText.trim()) {
        setProblem(t('online.join.deckTextEmpty'))
        return
      }
      deck = { deckText }
    }
    if (guest && !nickname.trim()) {
      setProblem(t('online.join.nicknameEmpty'))
      return
    }
    setWorking(true)
    try {
      const access = await joinGame(request, gameId, inviteSecret, deck, nickname.trim() || undefined)
      storeSeat(access)
      onJoined(access)
    } catch (caught) {
      if (caught instanceof ApiError && caught.code === 'deck_text_invalid' && Array.isArray(caught.details.lineErrors)) {
        setLineErrors(caught.details.lineErrors as DeckTextLineError[])
      } else {
        setError(caught)
      }
      setWorking(false)
    }
  }

  return (
    <form className="form panel" onSubmit={(event) => void submit(event)} noValidate>
      <h2>{t('online.join.title')}</h2>
      {guest ? <p className="field-hint">{t('online.join.guestHint')}</p> : <p className="field-hint">{t('online.join.accountHint')}</p>}
      <TextField
        label={guest ? t('online.join.nickname') : t('online.join.nicknameOptional')}
        field="nickname"
        error={error}
        value={nickname}
        maxLength={joinLimits.nicknameMax}
        autoComplete="nickname"
        hint={guest ? undefined : t('online.join.nicknameHint')}
        onChange={(event) => setNickname(event.target.value)}
      />
      <fieldset className="field" disabled={working}>
        <legend>{t('online.join.deckSource')}</legend>
        {(['deck', 'text'] as const).map((value) => (
          <label key={value} className="table-choice">
            <input type="radio" name={`${id}-source`} value={value} checked={source === value} onChange={() => setSource(value)} />
            {t(`online.join.sources.${value}`)}
          </label>
        ))}
      </fieldset>
      {source === 'deck' ? (
        <TextField
          label={t('online.join.deckLink')}
          field="deckId"
          error={error}
          value={deckLink}
          hint={guest ? t('online.join.deckLinkGuestHint') : t('online.join.deckLinkHint')}
          onChange={(event) => setDeckLink(event.target.value)}
        />
      ) : (
        <div className="field">
          <label htmlFor={`${id}-text`}>{t('online.join.deckText')}</label>
          <textarea
            id={`${id}-text`}
            className="import-text"
            rows={10}
            spellCheck={false}
            maxLength={joinLimits.deckTextMax}
            aria-describedby={`${id}-text-hint`}
            value={deckText}
            onChange={(event) => setDeckText(event.target.value)}
          />
          <p className="field-hint" id={`${id}-text-hint`}>
            {t('online.join.deckTextHint')}
          </p>
        </div>
      )}
      {problem && (
        <p className="alert alert-error" role="alert">
          {problem}
        </p>
      )}
      <LineErrorList title={t('online.join.lineErrors')} lines={lineErrors} />
      <ErrorAlert error={error} />
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={working}>
          {t('online.join.submit')}
        </button>
      </div>
    </form>
  )
}
