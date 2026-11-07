import { useEffect, useId, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { sourceKey, type AutoTagLine, type AutoTagReason, type AutoTagResponse, type ApplyAutoTagRequest } from '../../api/autoTag'
import { ApiError } from '../../api/client'
import { categories, type Deck } from '../../api/decks'
import { useAuth } from '../../auth/session'
import { ErrorAlert } from '../Forms'

interface Choice {
  selected: boolean
  categories: string[]
}

type Loaded = { state: 'loading' } | { state: 'error'; error: unknown } | { state: 'done'; response: AutoTagResponse }

/** Keeps the known categories, in the order of the analysis. */
function ordered(values: string[]): string[] {
  return categories.filter((category) => values.includes(category))
}

function sameCategories(left: string[], right: string[]): boolean {
  return ordered(left).join() === ordered(right).join()
}

/**
 * The auto-tag dialog of the deck page: the suggested categories of each line, with their reasons.
 * The owner selects the lines, changes the categories when necessary, and applies the selection in one change.
 */
export function AutoTagDialog({ deck, onApplied, onClose }: { deck: Deck; onApplied(deck: Deck): void; onClose(): void }) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const id = useId()
  const [loaded, setLoaded] = useState<Loaded>({ state: 'loading' })
  const [choices, setChoices] = useState<Record<string, Choice>>({})
  const [onlyChanges, setOnlyChanges] = useState(true)
  const [editing, setEditing] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [conflict, setConflict] = useState(false)
  const [applying, setApplying] = useState(false)

  useEffect(() => {
    request<AutoTagResponse>(`/decks/${deck.id}/auto-tag`)
      .then((response) => {
        setChoices(Object.fromEntries(response.lines.map((line) => [line.lineId, { selected: line.changes, categories: ordered(line.suggested) }])))
        setLoaded({ state: 'done', response })
      })
      .catch((caught: unknown) => setLoaded({ state: 'error', error: caught }))
  }, [deck.id, request])

  const lines = useMemo(() => (loaded.state === 'done' ? loaded.response.lines : []), [loaded])
  const visible = lines.filter((line) => !onlyChanges || line.changes || choices[line.lineId]?.selected)
  const selected = lines.filter((line) => choices[line.lineId]?.selected)

  function update(lineId: string, change: Partial<Choice>) {
    setChoices((current) => ({ ...current, [lineId]: { ...current[lineId], ...change } }))
  }

  function toggleCategory(line: AutoTagLine, category: string) {
    const current = choices[line.lineId]?.categories ?? []
    const next = current.includes(category) ? current.filter((value) => value !== category) : [...current, category]
    update(line.lineId, { categories: ordered(next), selected: true })
  }

  function selectChanges(select: boolean) {
    setChoices((current) =>
      Object.fromEntries(lines.map((line) => [line.lineId, { ...current[line.lineId], selected: select && (line.changes || !sameCategories(current[line.lineId].categories, line.suggested)) }])),
    )
  }

  async function apply() {
    if (loaded.state !== 'done') {
      return
    }
    setError(null)
    setConflict(false)
    setApplying(true)
    const body: ApplyAutoTagRequest = {
      expectedVersion: loaded.response.version,
      lines: selected.map((line) => ({ lineId: line.lineId, categories: choices[line.lineId].categories })),
    }
    try {
      onApplied(await request<Deck>(`/decks/${deck.id}/auto-tag`, { method: 'POST', body }))
      onClose()
    } catch (caught) {
      if (caught instanceof ApiError && caught.code === 'deck_version_conflict') {
        setConflict(true)
      } else {
        setError(caught)
      }
    } finally {
      setApplying(false)
    }
  }

  return (
    <section className="dialog dialog-wide panel auto-tag" role="dialog" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t('autoTag.title')}</h2>
      <p className="field-hint">{t('autoTag.help')}</p>
      {loaded.state === 'loading' && <p role="status">{t('autoTag.loading')}</p>}
      {loaded.state === 'error' && <ErrorAlert error={loaded.error} />}
      {loaded.state === 'done' && lines.length === 0 && <p>{t('autoTag.empty')}</p>}
      {loaded.state === 'done' && lines.length > 0 && (
        <>
          <div className="auto-tag-toolbar">
            <label className="field-inline">
              <input type="checkbox" checked={onlyChanges} onChange={(event) => setOnlyChanges(event.target.checked)} />
              {t('autoTag.onlyChanges')}
            </label>
            <button type="button" className="button-small" onClick={() => selectChanges(true)}>
              {t('autoTag.selectChanges')}
            </button>
            <button type="button" className="button-small" onClick={() => selectChanges(false)}>
              {t('autoTag.selectNone')}
            </button>
          </div>
          {visible.length === 0 ? (
            <p>{t('autoTag.noChange')}</p>
          ) : (
            <table className="auto-tag-table">
              <caption className="visually-hidden">{t('autoTag.tableCaption')}</caption>
              <thead>
                <tr>
                  <th scope="col">{t('autoTag.columns.apply')}</th>
                  <th scope="col">{t('autoTag.columns.card')}</th>
                  <th scope="col">{t('autoTag.columns.current')}</th>
                  <th scope="col">{t('autoTag.columns.suggested')}</th>
                  <th scope="col">{t('autoTag.columns.reason')}</th>
                </tr>
              </thead>
              <tbody>
                {visible.map((line) => (
                  <AutoTagRow
                    key={line.lineId}
                    line={line}
                    choice={choices[line.lineId]}
                    editing={editing === line.lineId}
                    onSelect={(value) => update(line.lineId, { selected: value })}
                    onEdit={() => setEditing(editing === line.lineId ? null : line.lineId)}
                    onToggle={(category) => toggleCategory(line, category)}
                  />
                ))}
              </tbody>
            </table>
          )}
          <p className="field-hint">{t('autoTag.ruleHint')}</p>
        </>
      )}
      {conflict && (
        <p className="alert alert-error" role="alert">
          {t('autoTag.conflict')}
        </p>
      )}
      <ErrorAlert error={error} />
      <div className="form-actions">
        <button type="button" className="button-primary" disabled={selected.length === 0 || applying} onClick={() => void apply()}>
          {t('autoTag.apply', { count: selected.length })}
        </button>
        <button type="button" onClick={onClose}>
          {t('actions.cancel')}
        </button>
      </div>
    </section>
  )
}

interface RowProps {
  line: AutoTagLine
  choice: Choice
  editing: boolean
  onSelect(selected: boolean): void
  onEdit(): void
  onToggle(category: string): void
}

/** One line of the dialog. The category editor opens under the suggested categories. */
function AutoTagRow({ line, choice, editing, onSelect, onEdit, onToggle }: RowProps) {
  const { t } = useTranslation('decks')
  const id = useId()
  return (
    <tr>
      <td>
        <input type="checkbox" aria-label={t('autoTag.select', { name: line.name })} checked={choice.selected} onChange={(event) => onSelect(event.target.checked)} />
      </td>
      <th scope="row">{line.name}</th>
      <td>
        <CategoryBadges values={line.categories} />
      </td>
      <td>
        <CategoryBadges values={choice.categories} />{' '}
        <button type="button" className="button-link" aria-expanded={editing} aria-controls={`${id}-editor`} onClick={onEdit}>
          {t('autoTag.edit', { name: line.name })}
        </button>
        {editing && (
          <fieldset id={`${id}-editor`} className="auto-tag-editor">
            <legend>{t('autoTag.editorLegend', { name: line.name })}</legend>
            {categories.map((category) => (
              <label key={category} className="field-inline">
                <input type="checkbox" checked={choice.categories.includes(category)} onChange={() => onToggle(category)} />
                {t(`categories.${category}`)}
              </label>
            ))}
          </fieldset>
        )}
      </td>
      <td>
        <Reasons line={line} />
      </td>
    </tr>
  )
}

function CategoryBadges({ values }: { values: string[] }) {
  const { t } = useTranslation('decks')
  const known = ordered(values) as (typeof categories)[number][]
  if (known.length === 0) {
    return <span className="field-hint">{t('autoTag.noCategory')}</span>
  }
  return (
    <>
      {known.map((category) => (
        <span key={category} className="badge badge-category">
          {t(`categories.${category}`)}
        </span>
      ))}
    </>
  )
}

/** The reasons of a suggestion: the Oracle tags, the card text or a rule of the user. */
function Reasons({ line }: { line: AutoTagLine }) {
  const { t } = useTranslation('decks')
  if (line.reasons.length === 0) {
    return <span className="field-hint">{t(`autoTag.source.${sourceKey(line.source)}`)}</span>
  }
  const texts = [...new Set(line.reasons.map((reason: AutoTagReason) => {
    const source = sourceKey(reason.source)
    return source === 'oracle_tag' ? t('autoTag.reason.oracleTag', { tag: reason.detail ?? '' }) : t(`autoTag.source.${source}`)
  }))]
  return <span>{texts.join(' · ')}</span>
}
