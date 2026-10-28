import { useTranslation } from 'react-i18next'
import type { ReportItemType } from '../../api/moderation'

/** Tells the owner that an administrator hid their deck or their profile. It shows nothing for an item that is not hidden. */
export function HiddenNotice({ itemType, hidden }: { itemType: ReportItemType; hidden: boolean | undefined }) {
  const { t } = useTranslation('moderation')
  if (!hidden) {
    return null
  }
  return (
    <p className="alert alert-warning" role="status">
      {t(`hidden.${itemType}`)}
    </p>
  )
}
