import { useTranslation } from 'react-i18next'

/**
 * The banner of a plugin from a URL in the development mode. The project did not review this plugin.
 * Show it on each contribution of a development plugin.
 */
export function NotCheckedBanner({ name }: { name: string }) {
  const { t } = useTranslation()
  return (
    <p className="alert alert-warning plugin-not-checked" role="note">
      <strong>{t('plugins.notChecked')}</strong> {t('plugins.notCheckedDescription', { name })}
    </p>
  )
}
