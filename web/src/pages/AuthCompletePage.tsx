import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate, safeReturnUrl, useLocation } from '../router/navigation'

/** The page at the end of a sign-in with an OAuth provider. */
export function AuthCompletePage() {
  const { t } = useTranslation('auth')
  const { state } = useAuth()
  const location = useLocation()
  const error = location.searchParams.get('error')
  const returnUrl = safeReturnUrl(location.searchParams.get('returnUrl'))

  useEffect(() => {
    if (!error && state.status === 'authenticated') {
      navigate(returnUrl, { replace: true })
    }
  }, [error, state.status, returnUrl])

  const failed = error ?? (state.status === 'anonymous' ? 'external_sign_in_failed' : null)
  return (
    <section className="page page-narrow">
      <h1>{t('signIn.title')}</h1>
      {failed ? (
        <>
          <ErrorAlert error={new ApiError(0, failed, failed)} />
          <p>
            <Link to="/sign-in">{t('authComplete.signInLink')}</Link>
          </p>
        </>
      ) : (
        <p role="status">{t('authComplete.waiting')}</p>
      )}
    </section>
  )
}
