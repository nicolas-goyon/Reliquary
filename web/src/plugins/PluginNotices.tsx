import { useSyncExternalStore } from 'react'
import { useTranslation } from 'react-i18next'
import { currentNotices, dismissNotice, subscribeNotices } from './notices'

/**
 * Shows the notices that the plugins send with `ui.notify`. The core renders the text of the plugin as text, with the name of the plugin.
 */
export function PluginNotices() {
  const { t } = useTranslation()
  const notices = useSyncExternalStore(subscribeNotices, currentNotices)
  return (
    <div className="plugin-notices" aria-live="polite">
      {notices.map((notice) => (
        <div key={notice.key} className={`plugin-notice plugin-notice-${notice.tone}`}>
          <p>
            <strong>{notice.development ? t('plugins.noticeFromNotChecked', { name: notice.pluginName }) : t('plugins.noticeFrom', { name: notice.pluginName })}</strong>{' '}
            {notice.message}
          </p>
          <button type="button" className="link-button" onClick={() => dismissNotice(notice.key)}>
            {t('plugins.dismissNotice')}
          </button>
        </div>
      ))}
    </div>
  )
}
