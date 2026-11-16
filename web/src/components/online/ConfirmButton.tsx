import { useState } from 'react'
import { useTranslation } from 'react-i18next'

/** A button that asks for a second click before a destructive action. */
export function ConfirmButton({ label, warning, confirmLabel, onConfirm }: { label: string; warning: string; confirmLabel: string; onConfirm: () => void }) {
  const { t } = useTranslation('playtest')
  const [asking, setAsking] = useState(false)
  if (!asking) {
    return (
      <button type="button" onClick={() => setAsking(true)}>
        {label}
      </button>
    )
  }
  return (
    <span className="online-confirm" role="group" aria-label={label}>
      <span role="alert">{warning}</span>
      <button type="button" className="button-danger" onClick={onConfirm}>
        {confirmLabel}
      </button>
      <button type="button" onClick={() => setAsking(false)}>
        {t('online.cancel')}
      </button>
    </span>
  )
}
