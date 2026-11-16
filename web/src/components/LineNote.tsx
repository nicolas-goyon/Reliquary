import { useState, type FormEvent, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Deck, DeckLine } from '../api/decks'
import { useAuth } from '../auth/session'
import { ErrorAlert } from './Forms'

/** The maximum length of a note. The API uses the same limit. */
export const maxNoteLength = 500

interface LineNoteProps {
  deck: Deck
  line: DeckLine
  /** The display name of the card, for the labels of the controls. */
  name: string
  /** True when the user can change the note. */
  editable: boolean
  onChange(deck: Deck): void
}

/**
 * The note of a deck line ("why this card is here").
 * All users see the note. The owner can add, change and remove the note.
 */
export function LineNote({ deck, line, name, editable, onChange }: LineNoteProps) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const [draft, setDraft] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const fieldId = `note-${line.id}`

  function cancel() {
    setDraft(null)
    setError(null)
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (draft === null) {
      return
    }
    const note = draft.trim()
    if (note === line.note) {
      cancel()
      return
    }
    setBusy(true)
    setError(null)
    try {
      onChange(await request<Deck>(`/decks/${deck.id}/lines/${line.id}`, { method: 'PATCH', body: { note } }))
      setDraft(null)
    } catch (caught) {
      setError(caught)
    } finally {
      setBusy(false)
    }
  }

  function handleKeyDown(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      event.preventDefault()
      cancel()
    }
  }

  if (draft !== null) {
    return (
      <form className="line-note line-note-form" onSubmit={(event) => void save(event)} aria-busy={busy}>
        <label className="visually-hidden" htmlFor={fieldId}>
          {t('note.label', { name })}
        </label>
        <input id={fieldId} type="text" value={draft} maxLength={maxNoteLength} autoFocus onKeyDown={handleKeyDown} onChange={(event) => setDraft(event.target.value)} />
        <button type="submit" className="button-small button-primary" disabled={busy}>
          {t('note.save')}
        </button>
        <button type="button" className="button-small" onClick={cancel}>
          {t('actions.cancel')}
        </button>
        <ErrorAlert error={error} />
      </form>
    )
  }

  return (
    <span className="line-note">
      {line.note && <span className="line-note-text">{line.note}</span>}
      {editable && (
        <button type="button" className="button-link" aria-label={t(line.note ? 'note.editLabel' : 'note.addLabel', { name })} onClick={() => setDraft(line.note)}>
          {t(line.note ? 'note.edit' : 'note.add')}
        </button>
      )}
    </span>
  )
}
