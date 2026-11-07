import { useEffect, useId, useState, type CSSProperties, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { categoryKey, formatDecimal, largestCount, typeKey } from '../api/analysis'
import type { Page } from '../api/cards'
import type { Deck, DeckSummary } from '../api/decks'
import {
  compareUrl,
  countDeltas,
  curveDeltas,
  parseListReference,
  signed,
  type DeckHistory,
  type DeckVariant,
  type DeckVersion,
  type DeltaRow,
  type ListChoice,
  type ListComparison,
  type ListReference,
} from '../api/versions'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { ListChangesView } from '../components/versions/ListChangesView'
import '../components/versions/versions.css'
import { navigate, useLocation } from '../router/navigation'

type DeckOption = { id: string; name: string }
/** The result of the comparison of the lists in the query. The key tells the query of the result. */
type Result = { key: string; error: unknown } | { key: string; comparison: ListComparison }

/** The last point of the mana curve. It counts the cards with this mana value or more. */
const lastCurvePoint = 7

function toReference(choice: ListChoice): ListReference {
  return { deckId: choice.listId, versionId: choice.versionId }
}

/**
 * The comparison of two lists: working lists or versions of decks and variants.
 * The query gives the two lists, for example `?left=deckId:versionId&right=deckId`, so the user can share a comparison.
 */
export function ComparePage() {
  const { t } = useTranslation('versions')
  const { state, request } = useAuth()
  const location = useLocation()
  const leftParam = location.searchParams.get('left')
  const rightParam = location.searchParams.get('right')
  const [ownDecks, setOwnDecks] = useState<DeckOption[]>([])
  const [extraDecks, setExtraDecks] = useState<DeckOption[]>([])
  const [left, setLeft] = useState<ListChoice | null>(null)
  const [right, setRight] = useState<ListChoice | null>(null)
  const [result, setResult] = useState<Result | null>(null)

  useEffect(() => {
    if (state.status !== 'authenticated') {
      return
    }
    request<Page<DeckSummary>>('/decks?limit=100')
      .then((page) => setOwnDecks(page.items.map((deck) => ({ id: deck.id, name: deck.name }))))
      .catch(() => setOwnDecks([]))
  }, [request, state.status])

  // Fill the two choices from the query. A list can be a variant, so the page gets the deck to know its main deck.
  useEffect(() => {
    if (state.status === 'loading') {
      return
    }
    const sides: [string | null, (choice: ListChoice | null) => void][] = [
      [leftParam, setLeft],
      [rightParam, setRight],
    ]
    for (const [param, set] of sides) {
      const reference = parseListReference(param)
      if (!reference) {
        continue
      }
      void resolveChoice(reference)
        .then(({ choice, option }) => {
          set(choice)
          setExtraDecks((decks) => (decks.some((deck) => deck.id === option.id) ? decks : [...decks, option]))
        })
        .catch(() => set(null))
    }

    async function resolveChoice(reference: ListReference): Promise<{ choice: ListChoice; option: DeckOption }> {
      const deck = await request<Deck>(`/decks/${reference.deckId}`)
      const main = deck.variantOf ? await request<Deck>(`/decks/${deck.variantOf}`) : deck
      return { choice: { mainId: main.id, listId: deck.id, versionId: reference.versionId }, option: { id: main.id, name: main.name } }
    }
  }, [leftParam, request, rightParam, state.status])

  useEffect(() => {
    const leftReference = parseListReference(leftParam)
    const rightReference = parseListReference(rightParam)
    if (state.status === 'loading' || !leftReference || !rightReference) {
      return
    }
    const key = `${leftParam}|${rightParam}`
    request<ListComparison>('/lists/compare', { method: 'POST', body: { left: leftReference, right: rightReference } })
      .then((comparison) => setResult({ key, comparison }))
      .catch((error: unknown) => setResult({ key, error }))
  }, [leftParam, request, rightParam, state.status])

  const queryKey = parseListReference(leftParam) && parseListReference(rightParam) ? `${leftParam}|${rightParam}` : null
  const current = result && result.key === queryKey ? result : null

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (left && right) {
      navigate(compareUrl(toReference(left), toReference(right)))
    }
  }

  const decks = [...ownDecks, ...extraDecks.filter((extra) => !ownDecks.some((own) => own.id === extra.id))].sort((first, second) => first.name.localeCompare(second.name))

  return (
    <article className="page page-wide compare-page">
      <h1>{t('compare.title')}</h1>
      <p className="field-hint">{t('compare.help')}</p>
      <form className="compare-form" onSubmit={handleSubmit} noValidate>
        <div className="compare-pickers">
          <ListPicker legend={t('compare.first')} value={left} decks={decks} onChange={setLeft} />
          <ListPicker legend={t('compare.second')} value={right} decks={decks} onChange={setRight} />
        </div>
        <div className="form-actions">
          <button type="submit" className="button-primary" disabled={!left || !right}>
            {t('compare.submit')}
          </button>
          <button
            type="button"
            onClick={() => {
              setLeft(right)
              setRight(left)
            }}
          >
            {t('compare.swap')}
          </button>
        </div>
      </form>
      {queryKey && !current && <p role="status">{t('compare.loading')}</p>}
      {current && 'error' in current && <ErrorAlert error={current.error} />}
      {current && 'comparison' in current && <ComparisonResult comparison={current.comparison} />}
    </article>
  )
}

interface ListPickerProps {
  legend: string
  value: ListChoice | null
  decks: DeckOption[]
  onChange(choice: ListChoice | null): void
}

/** The choice of one list: a deck, the main deck or a variant, then the working list or a version. */
function ListPicker({ legend, value, decks, onChange }: ListPickerProps) {
  const { t } = useTranslation('versions')
  const { request } = useAuth()
  const id = useId()
  const [loadedVariants, setLoadedVariants] = useState<{ deckId: string; items: DeckVariant[] } | null>(null)
  const [loadedVersions, setLoadedVersions] = useState<{ deckId: string; items: DeckVersion[] } | null>(null)
  const mainId = value?.mainId
  const listId = value?.listId
  const variants = loadedVariants && loadedVariants.deckId === mainId ? loadedVariants.items : []
  const versions = loadedVersions && loadedVersions.deckId === listId ? loadedVersions.items : []

  useEffect(() => {
    if (!mainId) {
      return
    }
    request<DeckVariant[]>(`/decks/${mainId}/variants`)
      .then((items) => setLoadedVariants({ deckId: mainId, items }))
      .catch(() => setLoadedVariants({ deckId: mainId, items: [] }))
  }, [mainId, request])

  useEffect(() => {
    if (!listId) {
      return
    }
    request<DeckHistory>(`/decks/${listId}/versions`)
      .then((history) => setLoadedVersions({ deckId: listId, items: history.versions }))
      .catch(() => setLoadedVersions({ deckId: listId, items: [] }))
  }, [listId, request])

  return (
    <fieldset className="compare-picker">
      <legend>{legend}</legend>
      <div className="field">
        <label htmlFor={`${id}-deck`}>{t('compare.deck')}</label>
        <select id={`${id}-deck`} value={value?.mainId ?? ''} onChange={(event) => onChange(event.target.value ? { mainId: event.target.value, listId: event.target.value, versionId: null } : null)}>
          <option value="">{t('compare.chooseDeck')}</option>
          {decks.map((deck) => (
            <option key={deck.id} value={deck.id}>
              {deck.name}
            </option>
          ))}
        </select>
      </div>
      {value && variants.length > 0 && (
        <div className="field">
          <label htmlFor={`${id}-variant`}>{t('compare.variant')}</label>
          <select id={`${id}-variant`} value={value.listId} onChange={(event) => onChange({ ...value, listId: event.target.value, versionId: null })}>
            <option value={value.mainId}>{t('variants.mainDeck')}</option>
            {variants.map((variant) => (
              <option key={variant.id} value={variant.id}>
                {variant.name}
              </option>
            ))}
          </select>
        </div>
      )}
      {value && (
        <div className="field">
          <label htmlFor={`${id}-version`}>{t('compare.version')}</label>
          <select id={`${id}-version`} value={value.versionId ?? ''} onChange={(event) => onChange({ ...value, versionId: event.target.value || null })}>
            <option value="">{t('compare.workingList')}</option>
            {versions.map((version) => (
              <option key={version.id} value={version.id}>
                {t('createVariant.version', { number: version.number, message: version.message })}
              </option>
            ))}
          </select>
        </div>
      )}
    </fieldset>
  )
}

/** The result of a comparison: the numbers, the card changes, and the effect on the curve, the types and the categories. */
function ComparisonResult({ comparison }: { comparison: ListComparison }) {
  const { t, i18n } = useTranslation('versions')
  const { t: td } = useTranslation('decks')
  const language = i18n.resolvedLanguage ?? 'en'
  const { left, right } = comparison
  const first = t('compare.first')
  const second = t('compare.second')

  const categoryLabel = (key: string) => {
    const known = categoryKey(key)
    return known === 'uncategorized' ? td('analysis.categories.uncategorized') : td(`categories.${known}`)
  }

  return (
    <div className="compare-result">
      <section aria-labelledby="compare-summary-title">
        <h2 id="compare-summary-title">{t('compare.summary')}</h2>
        <table className="version-table">
          <caption className="visually-hidden">{t('compare.summary')}</caption>
          <thead>
            <tr>
              <th scope="col">{t('compare.measure')}</th>
              <th scope="col" className="version-number">
                {first}
              </th>
              <th scope="col" className="version-number">
                {second}
              </th>
              <th scope="col" className="version-number">
                {t('changes.difference')}
              </th>
            </tr>
          </thead>
          <tbody>
            {(
              [
                ['cards', td('analysis.summary.cards'), left.cardCount, right.cardCount],
                ['lands', td('analysis.summary.lands'), left.landCount, right.landCount],
                ['average', td('analysis.summary.averageManaValue'), left.averageManaValue, right.averageManaValue],
              ] as const
            ).map(([key, label, before, after]) => (
              <tr key={key}>
                <th scope="row">{label}</th>
                <td className="version-number">{formatDecimal(before, language)}</td>
                <td className="version-number">{formatDecimal(after, language)}</td>
                <td className="version-number">{signed(Math.round((after - before) * 100) / 100, language)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section aria-labelledby="compare-cards-title">
        <h2 id="compare-cards-title">{t('compare.cards')}</h2>
        <ListChangesView changes={comparison.changes} beforeLabel={first} afterLabel={second} />
      </section>

      <section aria-labelledby="compare-effect-title">
        <h2 id="compare-effect-title">{t('compare.effect')}</h2>
        <div className="compare-effects">
          <DeltaTable
            title={td('analysis.curve.title')}
            labelHeader={td('analysis.curve.manaValue')}
            rows={curveDeltas(left.manaCurve, right.manaCurve)}
            label={(key) => (Number(key) >= lastCurvePoint ? td('analysis.curve.orMore', { manaValue: key }) : key)}
            bars
          />
          <DeltaTable title={td('analysis.types.title')} labelHeader={td('analysis.types.type')} rows={countDeltas(left.types, right.types)} label={(key) => td(`analysis.types.keys.${typeKey(key)}`)} />
          <DeltaTable title={td('analysis.categories.title')} labelHeader={td('analysis.categories.category')} rows={countDeltas(left.categories, right.categories)} label={categoryLabel} />
        </div>
      </section>
    </div>
  )
}

interface DeltaTableProps {
  title: string
  labelHeader: string
  rows: DeltaRow[]
  label(key: string): string
  /** Shows a bar for each count, so the two lists are side by side. */
  bars?: boolean
}

/** A table of the counts of the two lists and their difference. */
function DeltaTable({ title, labelHeader, rows, label, bars = false }: DeltaTableProps) {
  const { t, i18n } = useTranslation('versions')
  const language = i18n.resolvedLanguage ?? 'en'
  const largest = largestCount(rows.flatMap((row) => [row.before, row.after]))

  function cell(value: number, side: 'before' | 'after') {
    return (
      <td className="version-number">
        {bars && (
          <span className="compare-bar-track" aria-hidden="true">
            <span className={`compare-bar compare-bar-${side}`} style={{ '--bar-size': `${(value / largest) * 100}%` } as CSSProperties} />
          </span>
        )}
        {value}
      </td>
    )
  }

  return (
    <table className="version-table compare-delta">
      <caption>{title}</caption>
      <thead>
        <tr>
          <th scope="col">{labelHeader}</th>
          <th scope="col" className="version-number">
            {t('compare.first')}
          </th>
          <th scope="col" className="version-number">
            {t('compare.second')}
          </th>
          <th scope="col" className="version-number">
            {t('changes.difference')}
          </th>
        </tr>
      </thead>
      <tbody>
        {rows.length === 0 ? (
          <tr>
            <td colSpan={4} className="field-hint">
              {t('compare.noCards')}
            </td>
          </tr>
        ) : (
          rows.map((row) => (
            <tr key={row.key} className={row.after === row.before ? undefined : 'compare-changed'}>
              <th scope="row">{label(row.key)}</th>
              {cell(row.before, 'before')}
              {cell(row.after, 'after')}
              <td className={row.after > row.before ? 'version-number version-up' : row.after < row.before ? 'version-number version-down' : 'version-number'}>
                {signed(row.after - row.before, language)}
              </td>
            </tr>
          ))
        )}
      </tbody>
    </table>
  )
}
