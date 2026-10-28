import { useCallback, useEffect, useState, type DragEvent, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { AutocompleteItem } from '../api/cards'
import { ApiError, apiRequest } from '../api/client'
import { formatKey, lineImage, zones, type Deck, type DeckLegality, type DeckLine, type DeckTextLineError, type Visibility, type Zone } from '../api/decks'
import { useAuth } from '../auth/session'
import { AnalysisSection } from '../components/analysis/AnalysisSection'
import { AutoTagDialog } from '../components/auto-tag/AutoTagDialog'
import { CardImage } from '../components/CardImage'
import { Combobox, type Suggestion } from '../components/Combobox'
import { FormatSelect, VisibilitySelect } from '../components/DeckFields'
import { DeckExportDialog } from '../components/DeckExportDialog'
import { DeckSocialBar } from '../components/DeckSocialBar'
import { DeckTable } from '../components/deck-table/DeckTable'
import { HiddenNotice } from '../components/moderation/HiddenNotice'
import { ErrorAlert, TextField } from '../components/Forms'
import { LineErrorList } from '../components/LineErrors'
import { ManaCost } from '../components/ManaCost'
import { RecommendationsPanel } from '../components/recommendations/RecommendationsPanel'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'
import { Slot } from '../slots/Slot'

type Loaded = { state: 'loading' } | { state: 'error'; error: unknown } | { state: 'done'; deck: Deck }

/** The deck page: the owner edits the deck, and the other users read it. */
export function DeckPage({ id }: { id: string }) {
  const { t } = useTranslation('decks')
  const { state, request } = useAuth()
  const [loaded, setLoaded] = useState<Loaded>({ state: 'loading' })

  useEffect(() => {
    if (state.status === 'loading') {
      return
    }
    request<Deck>(`/decks/${id}`)
      .then((deck) => setLoaded({ state: 'done', deck }))
      .catch((error: unknown) => setLoaded({ state: 'error', error }))
  }, [id, request, state.status])

  if (loaded.state === 'loading') {
    return <p role="status">{t('editor.loading')}</p>
  }
  if (loaded.state === 'error') {
    return <ErrorAlert error={loaded.error} />
  }
  return <DeckEditor deck={loaded.deck} onChange={(deck) => setLoaded({ state: 'done', deck })} />
}

interface EditorProps {
  deck: Deck
  onChange(deck: Deck): void
}

function DeckEditor({ deck, onChange }: EditorProps) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const [error, setError] = useState<unknown>(null)
  const [mode, setMode] = useState<'visual' | 'text' | 'table'>('visual')
  const [copying, setCopying] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [autoTagging, setAutoTagging] = useState(false)
  const [legality, setLegality] = useState<DeckLegality | null>(null)
  const owner = deck.isOwner

  useEffect(() => {
    request<DeckLegality>(`/decks/${deck.id}/legality`)
      .then(setLegality)
      .catch(() => setLegality(null))
  }, [deck.id, deck.version, request])

  /** Sends a change of the deck, and keeps the deck of the response. */
  const change = useCallback(
    async (path: string, method: string, body?: unknown) => {
      setError(null)
      try {
        onChange(await request<Deck>(`/decks/${deck.id}${path}`, { method, body }))
      } catch (caught) {
        setError(caught)
      }
    },
    [deck.id, onChange, request],
  )

  async function remove() {
    if (!window.confirm(t('editor.deleteConfirm', { name: deck.name }))) {
      return
    }
    try {
      await request(`/decks/${deck.id}`, { method: 'DELETE' })
      navigate('/decks')
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <article className="page page-wide deck-page">
      <DeckHeader deck={deck} onSave={(fields) => void change('', 'PATCH', fields)} />
      <DeckSocialBar deck={deck} />
      <HiddenNotice itemType="deck" hidden={deck.isOwner && deck.hidden} />
      <div className="deck-actions" role="toolbar" aria-label={t('editor.actions')}>
        {owner &&
          (['visual', 'text', 'table'] as const).map((value) => (
            <button key={value} type="button" aria-pressed={mode === value} onClick={() => setMode(value)}>
              {t(`editor.${value}Mode`)}
            </button>
          ))}
        {owner && (
          <button type="button" onClick={() => setAutoTagging(true)}>
            {t('autoTag.open')}
          </button>
        )}
        <button type="button" onClick={() => setCopying(true)}>
          {t('editor.copy')}
        </button>
        <button type="button" onClick={() => setExporting(true)}>
          {t('editor.export')}
        </button>
        {owner && (
          <button type="button" className="button-danger" onClick={() => void remove()}>
            {t('editor.delete')}
          </button>
        )}
        <Slot name="deck.actions" context={{ deckId: deck.id }} className="slot-inline" />
      </div>
      {copying && <CopyDialog deck={deck} onClose={() => setCopying(false)} />}
      {exporting && <DeckExportDialog deckId={deck.id} onClose={() => setExporting(false)} />}
      {autoTagging && owner && <AutoTagDialog deck={deck} onApplied={onChange} onClose={() => setAutoTagging(false)} />}
      <ErrorAlert error={error} />
      {legality && <LegalityPanel deck={deck} legality={legality} />}
      {owner && <RecommendationsPanel deck={deck} onAdd={(oracleId, zone) => change('/lines', 'POST', { lines: [{ oracleId, zone }] })} />}

      {mode === 'text' && owner ? (
        <TextMode deck={deck} onSaved={(saved) => {
          onChange(saved)
          setMode('visual')
        }} />
      ) : mode === 'table' && owner ? (
        <DeckTable deck={deck} onChange={onChange} />
      ) : (
        <>
          {owner && <AddCard onAdd={(oracleId, zone) => void change('/lines', 'POST', { lines: [{ oracleId, zone }] })} />}
          <Zones deck={deck} owner={owner} change={change} />
        </>
      )}
      <AnalysisSection deck={deck} />
    </article>
  )
}

/** The name, the format and the visibility of the deck. The owner can change them. */
function DeckHeader({ deck, onSave }: { deck: Deck; onSave(fields: { name?: string; format?: string; visibility?: Visibility }): void }) {
  const { t } = useTranslation('decks')
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(deck.name)

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setEditing(false)
    if (name.trim() && name.trim() !== deck.name) {
      onSave({ name: name.trim() })
    }
  }

  const total = deck.counts.commander + deck.counts.companion + deck.counts.main
  return (
    <header className="deck-header">
      {editing ? (
        <form className="form-inline" onSubmit={handleSubmit}>
          <TextField label={t('fields.name')} value={name} maxLength={100} autoFocus onChange={(event) => setName(event.target.value)} />
          <button type="submit" className="button-primary">
            {t('actions.save')}
          </button>
        </form>
      ) : (
        <h1>
          {deck.name}{' '}
          {deck.isOwner && (
            <button type="button" className="button-link" onClick={() => setEditing(true)}>
              {t('editor.rename')}
            </button>
          )}
        </h1>
      )}
      <p className="deck-meta">
        {t(`formats.${formatKey(deck.format)}`)} · {t('editor.cardCount', { count: total })} · {t(`visibility.${deck.visibility}`)}
        {deck.sourceDeckId && (
          <>
            {' · '}
            <Link to={`/decks/${deck.sourceDeckId}`}>{t('editor.original')}</Link>
          </>
        )}
      </p>
      {deck.isOwner && (
        <div className="deck-settings">
          <FormatSelect value={deck.format} onChange={(format) => onSave({ format })} />
          <VisibilitySelect value={deck.visibility} onChange={(visibility) => onSave({ visibility })} />
        </div>
      )}
    </header>
  )
}

/** The field that adds a card: autocomplete of the card names, and the zone of the new card. */
function AddCard({ onAdd }: { onAdd(oracleId: string, zone: Zone): void }) {
  const { t, i18n } = useTranslation('decks')
  const [text, setText] = useState('')
  const [zone, setZone] = useState<Zone>('main')

  const suggest = useCallback(
    async (value: string, signal: AbortSignal): Promise<Suggestion<string>[]> => {
      if (value.trim().length < 2) {
        return []
      }
      const items = await apiRequest<AutocompleteItem[]>(
        `/cards/autocomplete?q=${encodeURIComponent(value.trim())}&lang=${i18n.resolvedLanguage ?? 'en'}&limit=8${zone === 'tokens' ? '&includeExtras=true' : ''}`,
        { signal },
      )
      return items.map((item) => ({ key: item.oracleId, label: item.matchedName, detail: item.matchedName === item.name ? undefined : item.name, value: item.oracleId }))
    },
    [i18n.resolvedLanguage, zone],
  )

  return (
    <div className="add-card">
      <Combobox
        label={t('editor.addCard')}
        value={text}
        onChange={setText}
        suggest={suggest}
        onSelect={(suggestion) => {
          onAdd(suggestion.value, zone)
          setText('')
        }}
        placeholder={t('editor.addCardPlaceholder')}
        data-shortcut="add-card"
      />
      <div className="field">
        <label htmlFor="add-card-zone">{t('editor.zone')}</label>
        <select id="add-card-zone" value={zone} onChange={(event) => setZone(event.target.value as Zone)}>
          {zones.map((value) => (
            <option key={value} value={value}>
              {t(`zones.${value}`)}
            </option>
          ))}
        </select>
      </div>
    </div>
  )
}

/** The zones of the deck. The owner can drag a line to another zone, or use the zone field of the line. */
function Zones({ deck, owner, change }: { deck: Deck; owner: boolean; change(path: string, method: string, body?: unknown): Promise<void> }) {
  const { t } = useTranslation('decks')
  const [dropZone, setDropZone] = useState<Zone | null>(null)

  function handleDrop(event: DragEvent, zone: Zone) {
    event.preventDefault()
    setDropZone(null)
    const lineId = event.dataTransfer.getData('application/x-reliquary-line')
    const line = deck.lines.find((candidate) => candidate.id === lineId)
    if (line && line.zone !== zone) {
      void change(`/lines/${line.id}`, 'PATCH', { zone })
    }
  }

  return (
    <div className="zones">
      {zones
        .filter((zone) => owner || deck.counts[zone] > 0)
        .map((zone) => {
          const lines = deck.lines
            .filter((line) => line.zone === zone)
            .sort((left, right) => (deck.cards[left.oracleId]?.name ?? '').localeCompare(deck.cards[right.oracleId]?.name ?? ''))
          return (
            <section
              key={zone}
              className={dropZone === zone ? 'zone zone-drop' : 'zone'}
              aria-labelledby={`zone-${zone}`}
              onDragOver={owner ? (event) => {
                event.preventDefault()
                setDropZone(zone)
              } : undefined}
              onDragLeave={owner ? () => setDropZone(null) : undefined}
              onDrop={owner ? (event) => handleDrop(event, zone) : undefined}
            >
              <h2 id={`zone-${zone}`}>
                {t(`zones.${zone}`)} <span className="zone-count">({deck.counts[zone]})</span>
              </h2>
              {lines.length === 0 ? (
                <p className="field-hint">{t('editor.emptyZone')}</p>
              ) : (
                <ul className="deck-lines">
                  {lines.map((line) => (
                    <LineItem key={line.id} deck={deck} line={line} owner={owner} change={change} />
                  ))}
                </ul>
              )}
            </section>
          )
        })}
    </div>
  )
}

function LineItem({ deck, line, owner, change }: { deck: Deck; line: DeckLine; owner: boolean; change(path: string, method: string, body?: unknown): Promise<void> }) {
  const { t } = useTranslation('decks')
  const card = deck.cards[line.oracleId]
  const name = card?.name ?? line.oracleId
  const tags = line.tagIds.map((id) => deck.tags[id]?.name).filter(Boolean)
  return (
    <li
      className="deck-line"
      draggable={owner}
      onDragStart={(event) => {
        event.dataTransfer.setData('application/x-reliquary-line', line.id)
        event.dataTransfer.effectAllowed = 'move'
      }}
    >
      <span className="deck-line-quantity">
        {owner ? (
          <>
            <button type="button" className="button-small" aria-label={t('editor.decrease', { name })} onClick={() => void change(`/lines/${line.id}`, 'PATCH', { quantity: line.quantity - 1 })}>
              −
            </button>
            <span aria-label={t('editor.quantity', { name })}>{line.quantity}</span>
            <button type="button" className="button-small" aria-label={t('editor.increase', { name })} onClick={() => void change(`/lines/${line.id}`, 'PATCH', { quantity: line.quantity + 1 })}>
              +
            </button>
          </>
        ) : (
          <span>{line.quantity}</span>
        )}
      </span>
      <span className="deck-line-name">
        <Link to={`/cards/${line.oracleId}`}>{name}</Link>
        <span className="deck-line-preview" aria-hidden="true">
          <CardImage src={lineImage(deck, line)} alt="" />
        </span>
        {line.finish && line.finish !== 'nonfoil' && <span className="badge">{t(`finishes.${line.finish === 'etched' ? 'etched' : 'foil'}`)}</span>}
        {tags.map((tag) => (
          <span key={tag} className="badge badge-tag">
            #{tag}
          </span>
        ))}
        {line.categories.map((category) => (
          <span key={category} className="badge badge-category">
            {t(`categories.${categoryKey(category)}`)}
          </span>
        ))}
      </span>
      <ManaCost cost={card?.manaCost} />
      {owner && (
        <span className="deck-line-actions">
          <label className="visually-hidden" htmlFor={`zone-${line.id}`}>
            {t('editor.moveTo', { name })}
          </label>
          <select id={`zone-${line.id}`} value={line.zone} onChange={(event) => void change(`/lines/${line.id}`, 'PATCH', { zone: event.target.value })}>
            {zones.map((zone) => (
              <option key={zone} value={zone}>
                {t(`zones.${zone}`)}
              </option>
            ))}
          </select>
          <button type="button" className="button-small button-danger" aria-label={t('editor.remove', { name })} onClick={() => void change(`/lines/${line.id}`, 'DELETE')}>
            ×
          </button>
        </span>
      )}
    </li>
  )
}

const knownCategories = ['ramp', 'card_draw', 'targeted_interaction', 'board_wipe', 'synergy', 'land', 'win_condition', 'protection'] as const

function categoryKey(category: string): (typeof knownCategories)[number] {
  return knownCategories.find((known) => known === category) ?? 'synergy'
}

const knownProblems = [
  'deck_size', 'too_many_copies', 'sideboard_size', 'card_banned', 'card_not_legal', 'card_restricted', 'commander_missing',
  'commander_not_allowed', 'too_many_commanders', 'commanders_cannot_pair', 'outside_color_identity', 'companion_not_allowed',
] as const

function problemKey(code: string): (typeof knownProblems)[number] | 'other' {
  return knownProblems.find((known) => known === code) ?? 'other'
}

/** The legality of the deck in its format, with each problem. */
function LegalityPanel({ deck, legality }: { deck: Deck; legality: DeckLegality }) {
  const { t } = useTranslation('decks')
  if (legality.legal) {
    return (
      <p className="alert alert-success" role="status">
        {t('legality.legal', { format: t(`formats.${formatKey(deck.format)}`) })}
      </p>
    )
  }
  return (
    <section className="alert alert-error" aria-labelledby="legality-title">
      <h2 id="legality-title">{t('legality.title', { format: t(`formats.${formatKey(deck.format)}`) })}</h2>
      <ul>
        {legality.problems.map((problem, index) => (
          <li key={`${problem.code}-${problem.oracleId ?? index}`}>
            {t(`legality.problems.${problemKey(problem.code)}`, {
              card: problem.oracleId ? (deck.cards[problem.oracleId]?.name ?? '') : '',
              count: problem.count ?? 0,
              limit: problem.limit ?? 0,
            })}
          </li>
        ))}
      </ul>
    </section>
  )
}

/** The text mode: the full list as text. The save shows each line that the API cannot use. */
function TextMode({ deck, onSaved }: { deck: Deck; onSaved(deck: Deck): void }) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const [text, setText] = useState<string | null>(null)
  const [version, setVersion] = useState(deck.version)
  const [lineErrors, setLineErrors] = useState<DeckTextLineError[]>([])
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    request<{ text: string; version: number }>(`/decks/${deck.id}/text`)
      .then((response) => {
        setText(response.text)
        setVersion(response.version)
      })
      .catch((caught: unknown) => setError(caught))
  }, [deck.id, request])

  async function save() {
    setError(null)
    setLineErrors([])
    try {
      onSaved(await request<Deck>(`/decks/${deck.id}/text`, { method: 'PUT', body: { text, expectedVersion: version } }))
    } catch (caught) {
      if (caught instanceof ApiError && caught.code === 'deck_text_invalid' && Array.isArray(caught.details.lineErrors)) {
        setLineErrors(caught.details.lineErrors as DeckTextLineError[])
      } else {
        setError(caught)
      }
    }
  }

  if (text === null) {
    return <ErrorAlert error={error} />
  }

  return (
    <section className="text-mode" aria-labelledby="text-mode-title">
      <h2 id="text-mode-title">{t('text.title')}</h2>
      <p className="field-hint">{t('text.help')}</p>
      <label className="visually-hidden" htmlFor="deck-text">
        {t('text.label')}
      </label>
      <textarea id="deck-text" className="deck-text" rows={24} spellCheck={false} value={text} onChange={(event) => setText(event.target.value)} />
      <LineErrorList title={t('text.errors')} lines={lineErrors} />
      <ErrorAlert error={error} />
      <button type="button" className="button-primary" onClick={() => void save()}>
        {t('text.save')}
      </button>
    </section>
  )
}

/** The dialog of the copy of a deck: name, visibility and folder of the copy. */
function CopyDialog({ deck, onClose }: { deck: Deck; onClose(): void }) {
  const { t } = useTranslation('decks')
  const { state, request } = useAuth()
  const [name, setName] = useState(deck.isOwner ? t('copy.defaultName', { name: deck.name }) : deck.name)
  const [visibility, setVisibility] = useState<Visibility>('private')
  const [error, setError] = useState<unknown>(null)

  if (state.status !== 'authenticated') {
    return (
      <div className="dialog panel" role="dialog" aria-labelledby="copy-title">
        <h2 id="copy-title">{t('editor.copy')}</h2>
        <p>
          <Link to={`/sign-in?returnUrl=${encodeURIComponent(`/decks/${deck.id}`)}`}>{t('copy.signIn')}</Link>
        </p>
        <button type="button" onClick={onClose}>
          {t('actions.cancel')}
        </button>
      </div>
    )
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      const copy = await request<Deck>(`/decks/${deck.id}/copy`, { method: 'POST', body: { name, visibility } })
      onClose()
      navigate(`/decks/${copy.id}`)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <div className="dialog" role="dialog" aria-labelledby="copy-title">
      <form className="form panel" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <h2 id="copy-title">{t('editor.copy')}</h2>
        <TextField label={t('fields.name')} field="name" error={error} maxLength={100} value={name} onChange={(event) => setName(event.target.value)} />
        <VisibilitySelect value={visibility} onChange={setVisibility} />
        <ErrorAlert error={error} />
        <div className="form-actions">
          <button type="submit" className="button-primary">
            {t('copy.submit')}
          </button>
          <button type="button" onClick={onClose}>
            {t('actions.cancel')}
          </button>
        </div>
      </form>
    </div>
  )
}
