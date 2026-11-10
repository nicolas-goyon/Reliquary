import { useTranslation } from 'react-i18next'
import { Dialog } from '../components/versions/Dialog'
import { pluginText } from '../slots/pluginText'
import { isPluginPanel, useSlotContributions } from '../slots/registry'
import { SlotItem } from '../slots/Slot'

/**
 * The consumer of the slot "plugin.dialog": the dialogs that the plugins open with `ui.openDialog`.
 * The core shows each dialog with its own dialog component, a title and a close button.
 */
export function PluginDialogs() {
  const { t, i18n } = useTranslation()
  const dialogs = useSlotContributions('plugin.dialog').filter(isPluginPanel)
  return (
    <>
      {dialogs.map((dialog) => (
        <Dialog key={dialog.id} title={pluginText(dialog.title, i18n.resolvedLanguage)} onClose={() => dialog.close?.()}>
          <SlotItem slot="plugin.dialog" contribution={dialog} context={{}} />
          <div className="form-actions">
            <button type="button" onClick={() => dialog.close?.()}>
              {t('plugins.closeDialog')}
            </button>
          </div>
        </Dialog>
      ))}
    </>
  )
}
