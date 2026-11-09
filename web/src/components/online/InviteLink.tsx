import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { inviteLink } from '../../playtest/online/gameApi'

/** The invite link of a game, with a button that copies it. The link contains the invite secret: only the host sees it. */
export function InviteLink({ gameId, inviteSecret, onNewInvite }: { gameId: string; inviteSecret: string; onNewInvite?: () => void }) {
  const { t } = useTranslation('playtest')
  const id = useId()
  const [copied, setCopied] = useState(false)
  const link = inviteLink(gameId, inviteSecret)

  async function copy() {
    try {
      await navigator.clipboard.writeText(link)
      setCopied(true)
    } catch {
      // The browser refused the clipboard. The user can select the link in the field.
      setCopied(false)
    }
  }

  return (
    <div className="field online-invite">
      <label htmlFor={id}>{t('online.lobby.invite')}</label>
      <div className="online-invite-row">
        <input id={id} readOnly value={link} onFocus={(event) => event.target.select()} />
        <button type="button" onClick={() => void copy()}>
          {t('online.lobby.copy')}
        </button>
        {onNewInvite && (
          <button type="button" onClick={onNewInvite}>
            {t('online.lobby.newInvite')}
          </button>
        )}
      </div>
      <p className="field-hint" role="status">
        {copied ? t('online.lobby.copied') : t('online.lobby.inviteHint')}
      </p>
    </div>
  )
}
