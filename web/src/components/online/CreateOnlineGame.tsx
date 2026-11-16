import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../../auth/session'
import { createGame, gamePath, joinLimits, onlineSeats, storeSeat } from '../../playtest/online/gameApi'
import { navigate } from '../../router/navigation'
import { ErrorAlert, TextField } from '../Forms'

/** The form that creates an online game with a deck of the user. Only an account can create a game. */
export function CreateOnlineGame({ deckId }: { deckId: string }) {
  const { t } = useTranslation('playtest')
  const { state, request } = useAuth()
  const id = useId()
  const [seats, setSeats] = useState<number>(onlineSeats.max)
  // The name of the host in the game. The default is the display name of the account: the field shows it until the user types.
  const [typedNickname, setNickname] = useState<string | null>(null)
  const nickname = typedNickname ?? (state.status === 'authenticated' ? state.account.displayName : '')
  const [working, setWorking] = useState(false)
  const [error, setError] = useState<unknown>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setWorking(true)
    setError(null)
    try {
      const game = await createGame(request, deckId, seats, nickname.trim() || undefined)
      storeSeat(game)
      navigate(gamePath(game.gameId))
    } catch (caught) {
      setError(caught)
      setWorking(false)
    }
  }

  return (
    <form className="form panel" onSubmit={(event) => void submit(event)}>
      <h2>{t('online.create.title')}</h2>
      <p className="field-hint">{t('online.create.hint')}</p>
      <fieldset className="field" disabled={working}>
        <legend>{t('local.playerCount')}</legend>
        {Array.from({ length: onlineSeats.max - onlineSeats.min + 1 }, (_, index) => onlineSeats.min + index).map((value) => (
          <label key={value} className="table-choice">
            <input type="radio" name={`${id}-seats`} value={value} checked={seats === value} onChange={() => setSeats(value)} />
            {t('local.playerCountValue', { count: value })}
          </label>
        ))}
      </fieldset>
      <TextField
        label={t('online.join.nicknameOptional')}
        field="nickname"
        error={error}
        value={nickname}
        maxLength={joinLimits.nicknameMax}
        autoComplete="nickname"
        hint={t('online.join.nicknameHint')}
        disabled={working}
        onChange={(event) => setNickname(event.target.value)}
      />
      <ErrorAlert error={error} />
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={working}>
          {t('online.create.submit')}
        </button>
      </div>
    </form>
  )
}
