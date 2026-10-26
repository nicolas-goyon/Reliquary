import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError, apiRequest } from '../api/client'
import type { ProvidersResponse } from '../api/types'
import { useAuth } from '../auth/session'
import { ErrorAlert, SuccessAlert, TextField } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate, safeReturnUrl, useLocation } from '../router/navigation'

const providerNames: Record<string, string> = { google: 'Google', github: 'GitHub', discord: 'Discord' }

/** The sign-in page: a password form and the buttons of the OAuth providers. */
export function SignInPage() {
  const { t, i18n } = useTranslation('auth')
  const { signIn } = useAuth()
  const location = useLocation()
  const returnUrl = safeReturnUrl(location.searchParams.get('returnUrl'))
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [resent, setResent] = useState(false)
  const [providers, setProviders] = useState<string[]>([])

  useEffect(() => {
    const controller = new AbortController()
    apiRequest<ProvidersResponse>('/auth/providers', { signal: controller.signal })
      .then((response) => setProviders(response.providers))
      .catch(() => setProviders([]))
    return () => controller.abort()
  }, [])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    setResent(false)
    try {
      await signIn(email, password)
      navigate(returnUrl, { replace: true })
    } catch (caught) {
      setError(caught)
    } finally {
      setBusy(false)
    }
  }

  async function resendEmailCheck() {
    await apiRequest('/auth/email-check/resend', { method: 'POST', body: { email } }).catch(() => undefined)
    setResent(true)
  }

  const language = i18n.resolvedLanguage ?? 'en'
  const emailNotChecked = error instanceof ApiError && error.code === 'email_not_checked'

  return (
    <section className="page page-narrow">
      <h1>{t('signIn.title')}</h1>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <TextField label={t('fields.email')} type="email" autoComplete="email" required value={email} onChange={(event) => setEmail(event.target.value)} />
        <TextField
          label={t('fields.password')}
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
        <ErrorAlert error={error} />
        {emailNotChecked && !resent && (
          <button type="button" className="button-link" onClick={() => void resendEmailCheck()}>
            {t('signIn.resendEmailCheck')}
          </button>
        )}
        {resent && <SuccessAlert>{t('signIn.emailCheckResent')}</SuccessAlert>}
        <button type="submit" className="button-primary" disabled={busy}>
          {t('signIn.submit')}
        </button>
      </form>

      {providers.length > 0 && (
        <div className="providers">
          <p>{t('signIn.withProvider')}</p>
          {providers.map((provider) => (
            <a
              key={provider}
              className="button"
              href={`/api/v1/auth/external/${encodeURIComponent(provider)}?returnUrl=${encodeURIComponent(returnUrl)}&language=${language}`}
            >
              {providerNames[provider] ?? provider}
            </a>
          ))}
        </div>
      )}

      <p>
        <Link to="/forgot-password">{t('signIn.forgotPassword')}</Link>
      </p>
      <p>
        {t('signIn.noAccount')} <Link to="/register">{t('signIn.registerLink')}</Link>
      </p>
    </section>
  )
}
