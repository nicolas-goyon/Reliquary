import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Deck } from '../../api/decks'
import { ProxyDialog } from './ProxyDialog'

/** The "Print proxies" button of the deck actions, and its dialog. Each user who can see the deck can use it. */
export function ProxyAction({ deck }: { deck: Deck }) {
  const { t } = useTranslation('decks')
  const [open, setOpen] = useState(false)
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        {t('proxies.open')}
      </button>
      {open && <ProxyDialog deck={deck} onClose={() => setOpen(false)} />}
    </>
  )
}
