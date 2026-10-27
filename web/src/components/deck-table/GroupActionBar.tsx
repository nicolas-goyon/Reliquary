import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { categories, zones, type Zone } from '../../api/decks'
import type { GroupChange } from './deckTable'

const finishes = ['nonfoil', 'foil', 'etched'] as const

interface GroupActionBarProps {
  /** The number of selected lines. The actions are disabled when it is 0. */
  count: number
  /** True while a group action runs. The actions are then disabled. */
  busy: boolean
  /** The names of the tags of the deck, for the suggestions. */
  tagNames: string[]
  onApply(change: GroupChange): void
}

/** The toolbar of the group actions: each action changes all the selected lines in one request. */
export function GroupActionBar({ count, busy, tagNames, onApply }: GroupActionBarProps) {
  const { t } = useTranslation('decks')
  const [zone, setZone] = useState<Zone>('considering')
  const [tag, setTag] = useState('')
  const [global, setGlobal] = useState(false)
  const [category, setCategory] = useState<string>(categories[0])
  const [finish, setFinish] = useState<string>('nonfoil')
  const disabled = count === 0 || busy

  function applyTag(action: 'tag' | 'untag') {
    const name = tag.trim()
    if (name) {
      onApply(global ? { action, globalTags: [name] } : { action, tags: [name] })
    }
  }

  function removeLines() {
    if (window.confirm(t('table.removeConfirm', { count }))) {
      onApply({ action: 'remove' })
    }
  }

  return (
    <div className="group-actions" role="toolbar" aria-label={t('table.groupActions')}>
      <p className="group-actions-count" role="status">
        {t('table.selected', { count })}
      </p>
      <div className="group-action">
        <label htmlFor="group-zone">{t('table.zone')}</label>
        <select id="group-zone" value={zone} onChange={(event) => setZone(event.target.value as Zone)}>
          {zones.map((value) => (
            <option key={value} value={value}>
              {t(`zones.${value}`)}
            </option>
          ))}
        </select>
        <button type="button" disabled={disabled} onClick={() => onApply({ action: 'move', zone })}>
          {t('table.move')}
        </button>
      </div>
      <div className="group-action">
        <label htmlFor="group-tag">{t('table.tag')}</label>
        <input id="group-tag" list="group-tag-names" value={tag} maxLength={50} onChange={(event) => setTag(event.target.value)} />
        <datalist id="group-tag-names">
          {tagNames.map((name) => (
            <option key={name} value={name} />
          ))}
        </datalist>
        <label className="checkbox">
          <input type="checkbox" checked={global} onChange={(event) => setGlobal(event.target.checked)} />
          {t('table.globalTag')}
        </label>
        <button type="button" disabled={disabled || !tag.trim()} onClick={() => applyTag('tag')}>
          {t('table.addTag')}
        </button>
        <button type="button" disabled={disabled || !tag.trim()} onClick={() => applyTag('untag')}>
          {t('table.removeTag')}
        </button>
      </div>
      <div className="group-action">
        <label htmlFor="group-category">{t('table.category')}</label>
        <select id="group-category" value={category} onChange={(event) => setCategory(event.target.value)}>
          {categories.map((value) => (
            <option key={value} value={value}>
              {t(`categories.${value}`)}
            </option>
          ))}
        </select>
        <button type="button" disabled={disabled} onClick={() => onApply({ action: 'tag', categories: [category] })}>
          {t('table.addCategory')}
        </button>
        <button type="button" disabled={disabled} onClick={() => onApply({ action: 'untag', categories: [category] })}>
          {t('table.removeCategory')}
        </button>
      </div>
      <div className="group-action">
        <label htmlFor="group-finish">{t('table.finish')}</label>
        <select id="group-finish" value={finish} onChange={(event) => setFinish(event.target.value)}>
          {finishes.map((value) => (
            <option key={value} value={value}>
              {t(`finishes.${value}`)}
            </option>
          ))}
        </select>
        <button type="button" disabled={disabled} onClick={() => onApply({ action: 'printing', finish })}>
          {t('table.setFinish')}
        </button>
        <button type="button" disabled={disabled} onClick={() => onApply({ action: 'printing', clearPrinting: true })}>
          {t('table.defaultPrinting')}
        </button>
      </div>
      <div className="group-action">
        <button type="button" disabled={disabled} onClick={() => onApply({ action: 'quantity', quantityDelta: 1 })}>
          {t('table.addOne')}
        </button>
        <button type="button" disabled={disabled} onClick={() => onApply({ action: 'quantity', quantityDelta: -1 })}>
          {t('table.removeOne')}
        </button>
        <button type="button" className="button-danger" disabled={disabled} onClick={removeLines}>
          {t('table.removeLines')}
        </button>
      </div>
    </div>
  )
}
