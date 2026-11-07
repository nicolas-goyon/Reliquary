import { useId, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  buildQuery,
  cardTypes,
  colorLetters,
  colorModes,
  emptyFilters,
  filterFormats,
  manaValueOperators,
  parseQuery,
  rarities,
  splitTerms,
  type ColorFilter,
  type ColorMode,
  type FilterFormat,
  type ManaValueOperator,
  type Rarity,
  type SearchFilters,
} from '../api/searchFilters'

const manaClasses = { w: 'white', u: 'blue', b: 'black', r: 'red', g: 'green' } as const
const operatorKeys = { '=': 'equal', '<': 'less', '>': 'greater', '<=': 'lessOrEqual', '>=': 'greaterOrEqual' } as const

interface SearchFilterPanelProps {
  /** The text of the search field. */
  query: string
  /** Receives the new text of the search field when a filter changes. */
  onChange: (query: string) => void
}

/**
 * The visual filters of the card search. The panel reads its state from the search text and writes its terms into it.
 * The terms that the panel does not know stay in the text.
 */
export function SearchFilterPanel({ query, onChange }: SearchFilterPanelProps) {
  const { t } = useTranslation('cards')
  const id = useId()
  const parsed = useMemo(() => parseQuery(query), [query])
  const [open, setOpen] = useState(() => parsed.rest.length < splitTerms(query).length)
  // The comparison and the operator stay in the panel when no term of the query sets them.
  const [choices, setChoices] = useState(emptyFilters)

  const filters: SearchFilters = {
    ...parsed.filters,
    color: parsed.present.color ? parsed.filters.color : { ...choices.color, colors: [] },
    identity: parsed.present.identity ? parsed.filters.identity : { ...choices.identity, colors: [] },
    manaValue: parsed.present.manaValue ? parsed.filters.manaValue : { ...choices.manaValue, value: '' },
  }

  function update(change: Partial<SearchFilters>) {
    const next = { ...filters, ...change }
    setChoices(next)
    onChange(buildQuery(parsed.rest, next))
  }

  function toggle<T>(values: T[], value: T): T[] {
    return values.includes(value) ? values.filter((item) => item !== value) : [...values, value]
  }

  function colorGroup(key: 'color' | 'identity') {
    const filter: ColorFilter = filters[key]
    const set = (value: ColorFilter) => update(key === 'color' ? { color: value } : { identity: value })
    return (
      <fieldset className="filter-group">
        <legend>{t(`filters.${key}`)}</legend>
        <div className="filter-options">
          {colorLetters.map((letter) => (
            <label key={letter} className="filter-check">
              <input
                type="checkbox"
                checked={filter.colors.includes(letter)}
                onChange={() => set({ ...filter, colors: toggle(filter.colors, letter) })}
              />
              <span className={`mana-symbol mana-${manaClasses[letter]}`} aria-hidden="true">
                {letter.toUpperCase()}
              </span>
              {t(`filters.colorNames.${letter}`)}
            </label>
          ))}
        </div>
        <div className="field">
          <label htmlFor={`${id}-${key}-mode`}>{t('filters.mode')}</label>
          <select
            id={`${id}-${key}-mode`}
            value={filter.mode}
            onChange={(event) => set({ ...filter, mode: event.target.value as ColorMode })}
          >
            {colorModes.map((mode) => (
              <option key={mode} value={mode}>
                {t(`filters.modes.${mode}`)}
              </option>
            ))}
          </select>
        </div>
      </fieldset>
    )
  }

  return (
    <div className="search-filters">
      <button type="button" className="search-filters-toggle" aria-expanded={open} aria-controls={`${id}-panel`} onClick={() => setOpen(!open)}>
        {t('filters.toggle')}
      </button>
      {open && (
        <div id={`${id}-panel`} className="search-filters-panel">
          <p className="field-hint">{t('filters.help')}</p>
          <div className="filter-grid">
            {colorGroup('color')}
            {colorGroup('identity')}

            <fieldset className="filter-group">
              <legend>{t('filters.types')}</legend>
              <div className="filter-options">
                {cardTypes.map((type) => (
                  <label key={type} className="filter-check">
                    <input type="checkbox" checked={filters.types.includes(type)} onChange={() => update({ types: toggle(filters.types, type) })} />
                    {t(`filters.typeNames.${type}`)}
                  </label>
                ))}
              </div>
            </fieldset>

            <fieldset className="filter-group">
              <legend>{t('filters.manaValue')}</legend>
              <div className="filter-inline">
                <div className="field">
                  <label htmlFor={`${id}-mv-operator`}>{t('filters.operator')}</label>
                  <select
                    id={`${id}-mv-operator`}
                    value={filters.manaValue.operator}
                    onChange={(event) => update({ manaValue: { ...filters.manaValue, operator: event.target.value as ManaValueOperator } })}
                  >
                    {manaValueOperators.map((operator) => (
                      <option key={operator} value={operator}>
                        {`${operator} ${t(`filters.operators.${operatorKeys[operator]}`)}`}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="field">
                  <label htmlFor={`${id}-mv-value`}>{t('filters.value')}</label>
                  <input
                    id={`${id}-mv-value`}
                    type="number"
                    inputMode="numeric"
                    min={0}
                    step={1}
                    value={filters.manaValue.value}
                    onChange={(event) => update({ manaValue: { ...filters.manaValue, value: event.target.value } })}
                  />
                </div>
              </div>
            </fieldset>

            <div className="filter-group filter-fields">
              <div className="field">
                <label htmlFor={`${id}-rarity`}>{t('filters.rarity')}</label>
                <select id={`${id}-rarity`} value={filters.rarity} onChange={(event) => update({ rarity: event.target.value as Rarity | '' })}>
                  <option value="">{t('filters.anyRarity')}</option>
                  {rarities.map((rarity) => (
                    <option key={rarity} value={rarity}>
                      {t(`filters.rarities.${rarity}`)}
                    </option>
                  ))}
                </select>
              </div>
              <div className="field">
                <label htmlFor={`${id}-format`}>{t('filters.format')}</label>
                <select id={`${id}-format`} value={filters.format} onChange={(event) => update({ format: event.target.value as FilterFormat | '' })}>
                  <option value="">{t('filters.anyFormat')}</option>
                  {filterFormats.map((format) => (
                    <option key={format} value={format}>
                      {t(`formats.${format}`)}
                    </option>
                  ))}
                </select>
              </div>
              <div className="field">
                <label htmlFor={`${id}-set`}>{t('filters.set')}</label>
                <input
                  id={`${id}-set`}
                  value={filters.set}
                  aria-describedby={`${id}-set-hint`}
                  spellCheck={false}
                  autoCapitalize="off"
                  onChange={(event) => update({ set: event.target.value.replace(/[^a-z0-9]/gi, '') })}
                />
                <p id={`${id}-set-hint`} className="field-hint">
                  {t('filters.setHint')}
                </p>
              </div>
            </div>
          </div>
          <button type="button" onClick={() => update(emptyFilters())}>
            {t('filters.clear')}
          </button>
        </div>
      )}
    </div>
  )
}
