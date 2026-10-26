import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/session'
import { navigate } from '../router/navigation'
import { Slot } from '../slots/Slot'

/** The account page. The panels come from the slot "account.page". */
export function AccountPage() {
  const { t } = useTranslation('account')
  const { state } = useAuth()

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent('/account')}`, { replace: true })
    }
  }, [state.status])

  if (state.status !== 'authenticated') {
    return null
  }

  return (
    <section className="page">
      <h1>{t('title')}</h1>
      <Slot name="account.page" context={{ account: state.account }} className="panels" />
    </section>
  )
}
