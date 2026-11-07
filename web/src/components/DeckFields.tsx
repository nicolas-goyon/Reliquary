import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { deckFormats, visibilities, type Visibility } from '../api/decks'

/** The format field of a deck. */
export function FormatSelect({ value, onChange, disabled }: { value: string; onChange(value: string): void; disabled?: boolean }) {
  const { t } = useTranslation('decks')
  const id = useId()
  return (
    <div className="field">
      <label htmlFor={id}>{t('fields.format')}</label>
      <select id={id} value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
        {deckFormats.map((format) => (
          <option key={format} value={format}>
            {t(`formats.${format}`)}
          </option>
        ))}
      </select>
    </div>
  )
}

/** The visibility field of a deck, with a hint about who can open the deck. */
export function VisibilitySelect({ value, onChange, disabled }: { value: Visibility; onChange(value: Visibility): void; disabled?: boolean }) {
  const { t } = useTranslation('decks')
  const id = useId()
  return (
    <div className="field">
      <label htmlFor={id}>{t('fields.visibility')}</label>
      <select id={id} value={value} disabled={disabled} onChange={(event) => onChange(event.target.value as Visibility)}>
        {visibilities.map((visibility) => (
          <option key={visibility} value={visibility}>
            {t(`visibility.${visibility}`)}
          </option>
        ))}
      </select>
      <p className="field-hint">{t(`visibilityHint.${value}`)}</p>
    </div>
  )
}
