import { useSyncExternalStore } from 'react'
import { useTranslation } from 'react-i18next'
import { applyUpdate, isUpdateAvailable, subscribeToUpdate } from '../pwa/registration'

/** Tells the user that a new version of the web app is available, and lets the user load it. */
export function UpdatePrompt() {
  const { t } = useTranslation()
  const available = useSyncExternalStore(subscribeToUpdate, isUpdateAvailable)
  if (!available) {
    return null
  }
  return (
    <div className="update-prompt" role="status">
      <p>{t('update.available')}</p>
      <button type="button" className="button-primary" onClick={applyUpdate}>
        {t('update.apply')}
      </button>
    </div>
  )
}
