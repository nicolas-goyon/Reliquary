import { useTranslation } from 'react-i18next'

/**
 * The banner of a plugin from a URL in the development mode. The project did not review this plugin.
 * Show it on each contribution of a development plugin.
 * The compact form is a badge for a button or a menu item. Its tooltip and its accessible description give the full text.
 */
export function NotCheckedBanner({ name, compact = false }: { name: string; compact?: boolean }) {
  const { t } = useTranslation()
  const description = t('plugins.notCheckedDescription', { name })
  if (compact) {
    return (
      <span className="badge badge-warning plugin-not-checked-badge" title={description}>
        {t('plugins.notChecked')}
        <span className="visually-hidden"> {description}</span>
      </span>
    )
  }
  return (
    <p className="alert alert-warning plugin-not-checked" role="note">
      <strong>{t('plugins.notChecked')}</strong> {description}
    </p>
  )
}
