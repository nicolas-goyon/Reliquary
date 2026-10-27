import { useTranslation } from 'react-i18next'
import type { DeckTextLineError } from '../api/decks'

const knownLineErrors = [
  'missing_name',
  'quantity_out_of_range',
  'tag_too_long',
  'unknown_category',
  'card_not_found',
  'printing_not_found',
  'invalid_file',
  'missing_column',
  'unknown_zone',
] as const

function lineErrorKey(code: string): (typeof knownLineErrors)[number] | 'other' {
  return knownLineErrors.find((known) => known === code) ?? 'other'
}

/** The list of the lines that the API cannot use, or of the warnings of an import. */
export function LineErrorList({ title, lines, kind = 'error' }: { title: string; lines: DeckTextLineError[]; kind?: 'error' | 'warning' }) {
  const { t } = useTranslation('decks')
  if (lines.length === 0) {
    return null
  }
  return (
    <div className={kind === 'error' ? 'alert alert-error' : 'alert alert-warning'} role={kind === 'error' ? 'alert' : 'status'}>
      <p>{title}</p>
      <ul>
        {lines.map((lineError) => (
          <li key={`${lineError.line}-${lineError.code}`}>{t(`text.lineError.${lineErrorKey(lineError.code)}`, { line: lineError.line, text: lineError.text.trim() })}</li>
        ))}
      </ul>
    </div>
  )
}
