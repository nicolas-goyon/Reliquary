import { useState, type MouseEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { categories, zones, type Deck, type Zone } from '../../api/decks'
import { useAuth } from '../../auth/session'
import { Link } from '../../router/Link'
import { useSlotContributions } from '../../slots/registry'
import { SlotItem } from '../../slots/Slot'
import { ErrorAlert } from '../Forms'
import { GroupActionBar } from './GroupActionBar'
import { clickLine, emptySelection, lineName, noFilters, visibleLines, type GroupChange, type LineGroupBody, type Selection, type SortKey, type TableFilters, type TableSort } from './deckTable'

const columns: { key: SortKey; label: 'name' | 'zone' | 'quantity' | 'manaValue' | 'type' }[] = [
  { key: 'quantity', label: 'quantity' },
  { key: 'name', label: 'name' },
  { key: 'zone', label: 'zone' },
  { key: 'manaValue', label: 'manaValue' },
  { key: 'type', label: 'type' },
]

interface DeckTableProps {
  deck: Deck
  onChange(deck: Deck): void
}

/**
 * The table mode of the deck page: the lines in a table with sort, filters and a selection.
 * The group actions change all the selected lines in one request.
 */
export function DeckTable({ deck, onChange }: DeckTableProps) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const [filters, setFilters] = useState<TableFilters>(noFilters)
  const [sort, setSort] = useState<TableSort>({ key: 'name', direction: 'ascending' })
  const [selection, setSelection] = useState<Selection>(emptySelection)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const extraColumns = useSlotContributions('table.columns')

  const lines = visibleLines(deck, filters, sort)
  const order = lines.map((line) => line.id)
  // The actions apply only to the selected lines that the filters show.
  const selected = order.filter((id) => selection.ids.has(id))
  const allSelected = lines.length > 0 && selected.length === lines.length
  const tags = Object.entries(deck.tags).sort(([, left], [, right]) => left.name.localeCompare(right.name))

  function changeFilter(change: Partial<TableFilters>) {
    setFilters({ ...filters, ...change })
  }

  function changeSort(key: SortKey) {
    setSort(sort.key === key ? { key, direction: sort.direction === 'ascending' ? 'descending' : 'ascending' } : { key, direction: 'ascending' })
  }

  function handleRowClick(event: MouseEvent, id: string) {
    if ((event.target as HTMLElement).closest('a, button, input, select, label')) {
      return
    }
    setSelection(clickLine(selection, order, id, { range: event.shiftKey, toggle: event.ctrlKey || event.metaKey }))
  }

  function handleCheckboxClick(event: MouseEvent, id: string) {
    setSelection(clickLine(selection, order, id, { range: event.shiftKey, toggle: true }))
  }

  async function apply(change: GroupChange) {
    setError(null)
    setBusy(true)
    const body: LineGroupBody = { ...change, lineIds: selected, expectedVersion: deck.version }
    try {
      onChange(await request<Deck>(`/decks/${deck.id}/lines/group`, { method: 'POST', body }))
    } catch (caught) {
      setError(caught)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="deck-table" aria-labelledby="deck-table-title" aria-busy={busy}>
      <h2 id="deck-table-title" className="visually-hidden">
        {t('table.title')}
      </h2>
      <div className="deck-table-filters">
        <div className="field">
          <label htmlFor="table-filter-text">{t('table.filterName')}</label>
          <input id="table-filter-text" type="search" value={filters.text} onChange={(event) => changeFilter({ text: event.target.value })} />
        </div>
        <div className="field">
          <label htmlFor="table-filter-zone">{t('table.filterZone')}</label>
          <select id="table-filter-zone" value={filters.zone} onChange={(event) => changeFilter({ zone: event.target.value as Zone | '' })}>
            <option value="">{t('table.allZones')}</option>
            {zones.map((zone) => (
              <option key={zone} value={zone}>
                {t(`zones.${zone}`)}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="table-filter-label">{t('table.filterLabel')}</label>
          <select id="table-filter-label" value={filters.label} onChange={(event) => changeFilter({ label: event.target.value })}>
            <option value="">{t('table.allLabels')}</option>
            <optgroup label={t('table.tags')}>
              {tags.map(([id, tag]) => (
                <option key={id} value={`tag:${id}`}>
                  #{tag.name}
                </option>
              ))}
            </optgroup>
            <optgroup label={t('table.categories')}>
              {categories.map((category) => (
                <option key={category} value={`category:${category}`}>
                  {t(`categories.${category}`)}
                </option>
              ))}
            </optgroup>
          </select>
        </div>
      </div>

      <GroupActionBar count={selected.length} busy={busy} tagNames={[...new Set(tags.map(([, tag]) => tag.name))]} onApply={(change) => void apply(change)} />
      <ErrorAlert error={error} />

      <div className="deck-table-scroll">
        <table className="table deck-table-grid">
          <caption className="visually-hidden">{t('table.caption')}</caption>
          <thead>
            <tr>
              <th scope="col">
                <input
                  type="checkbox"
                  aria-label={t('table.selectAll')}
                  checked={allSelected}
                  disabled={lines.length === 0}
                  onChange={() => setSelection(allSelected ? emptySelection : { ids: new Set(order), anchor: null })}
                />
              </th>
              {columns.map((column) => (
                <th key={column.key} scope="col" aria-sort={sort.key === column.key ? sort.direction : undefined}>
                  <button type="button" className="button-link" onClick={() => changeSort(column.key)}>
                    {t(`table.columns.${column.label}`)}
                    {sort.key === column.key && <span aria-hidden="true">{sort.direction === 'ascending' ? ' ▲' : ' ▼'}</span>}
                  </button>
                </th>
              ))}
              <th scope="col">{t('table.columns.labels')}</th>
              <th scope="col">{t('table.columns.printing')}</th>
              {extraColumns.map((contribution) => (
                <th key={contribution.id} scope="col" data-contribution={contribution.id}>
                  <SlotItem slot="table.columns" contribution={contribution} context={{ deckId: deck.id, part: 'header' }} />
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {lines.length === 0 && (
              <tr>
                <td colSpan={8 + extraColumns.length}>{t('table.empty')}</td>
              </tr>
            )}
            {lines.map((line) => {
              const card = deck.cards[line.oracleId] ?? null
              const name = lineName(deck, line)
              const isSelected = selection.ids.has(line.id)
              const printing = line.printingId ? deck.printings[line.printingId] : undefined
              return (
                <tr
                  key={line.id}
                  aria-selected={isSelected}
                  className={isSelected ? 'deck-table-row deck-table-row-selected' : 'deck-table-row'}
                  onMouseDown={(event) => {
                    // Shift+click selects a range. It must not select the text of the page.
                    if (event.shiftKey) {
                      event.preventDefault()
                    }
                  }}
                  onClick={(event) => handleRowClick(event, line.id)}
                >
                  <td>
                    <input
                      type="checkbox"
                      aria-label={t('table.select', { name })}
                      checked={isSelected}
                      onClick={(event) => handleCheckboxClick(event, line.id)}
                      onChange={() => undefined}
                    />
                  </td>
                  <td>{line.quantity}</td>
                  <td>
                    <Link to={`/cards/${line.oracleId}`}>{name}</Link>
                  </td>
                  <td>{t(`zones.${line.zone}`)}</td>
                  <td>{card?.manaValue ?? ''}</td>
                  <td>{card?.typeLine ?? ''}</td>
                  <td>
                    {line.tagIds.map((id) => (
                      <span key={id} className="badge badge-tag">
                        #{deck.tags[id]?.name ?? ''}
                      </span>
                    ))}
                    {line.categories.map((category) => (
                      <span key={category} className="badge badge-category">
                        {categories.some((known) => known === category) ? t(`categories.${category as (typeof categories)[number]}`) : category}
                      </span>
                    ))}
                  </td>
                  <td>
                    {printing ? `${printing.setCode.toUpperCase()} ${printing.collectorNumber} (${printing.lang})` : t('table.defaultPrintingShort')}
                    {line.finish && <span className="badge">{t(`finishes.${line.finish === 'etched' ? 'etched' : line.finish === 'foil' ? 'foil' : 'nonfoil'}`)}</span>}
                  </td>
                  {extraColumns.map((contribution) => (
                    <td key={contribution.id} data-contribution={contribution.id}>
                      <SlotItem slot="table.columns" contribution={contribution} context={{ deckId: deck.id, part: 'cell', line, card }} />
                    </td>
                  ))}
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </section>
  )
}
