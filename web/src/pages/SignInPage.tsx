import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { apiRequest } from '../api/client'
import type { ProvidersResponse } from '../api/types'
import { useAuth } from '../auth/session'
import { ErrorAlert, TextField } from '../components/Forms'
import { navigate, safeReturnUrl, useLocation } from '../router/navigation'

const providerNames: Record<string, string> = { google: 'Google', github: 'GitHub', discord: 'Discord' }

/** The demo accounts of a local instance. `just up` loads them with the demo data. */
const demoAccounts = [
  { email: 'demo@reliquary.localhost', name: 'Demo' },
  { email: 'demo-fr@reliquary.localhost', name: 'Démo' },
  { email: 'admin@reliquary.localhost', name: 'Admin' },
]

/**
 * The sign-in page: one button for each OAuth provider of the instance.
 * A development instance also has the development sign-in, with no provider. See docs/decisions/0054-sign-in-with-oauth-providers-only.md.
 */
export function SignInPage() {
  const { t, i18n } = useTranslation('auth')
  const { developmentSignIn } = useAuth()
  const location = useLocation()
  const returnUrl = safeReturnUrl(location.searchParams.get('returnUrl'))
  const [methods, setMethods] = useState<ProvidersResponse | null>(null)
  const [email, setEmail] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    apiRequest<ProvidersResponse>('/auth/providers', { signal: controller.signal })
      .then(setMethods)
      .catch((caught: unknown) => {
        if (!controller.signal.aborted) {
          setError(caught)
        }
      })
    return () => controller.abort()
  }, [])

  async function signIn(address: string) {
    setBusy(true)
    setError(null)
    try {
      await developmentSignIn(address)
      navigate(returnUrl, { replace: true })
    } catch (caught) {
      setError(caught)
    } finally {
      setBusy(false)
    }
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    void signIn(email)
  }

  const language = i18n.resolvedLanguage ?? 'en'
  return (
    <section className="page page-narrow">
      <h1>{t('signIn.title')}</h1>

      {methods && methods.providers.length > 0 && (
        <div className="providers">
          <p>{t('signIn.withProvider')}</p>
          {methods.providers.map((provider) => (
            <a
              key={provider}
              className="button button-primary"
              href={`/api/v1/auth/external/${encodeURIComponent(provider)}?returnUrl=${encodeURIComponent(returnUrl)}&language=${language}`}
            >
              {t('signIn.provider', { provider: providerNames[provider] ?? provider })}
            </a>
          ))}
          <p className="field-hint">{t('signIn.firstTime')}</p>
        </div>
      )}

      {methods && methods.providers.length === 0 && !methods.developmentSignIn && (
        <p className="alert alert-error" role="alert">
          {t('signIn.noMethod')}
        </p>
      )}

      {methods?.developmentSignIn && (
        <section className="panel" aria-labelledby="development-sign-in-title">
          <h2 id="development-sign-in-title">{t('development.title')}</h2>
          <p>{t('development.description')}</p>
          <div className="providers">
            {demoAccounts.map((account) => (
              <button key={account.email} type="button" disabled={busy} onClick={() => void signIn(account.email)}>
                {t('development.demoAccount', { name: account.name })}
              </button>
            ))}
          </div>
          <form className="form" onSubmit={handleSubmit} noValidate>
            <TextField
              label={t('development.email')}
              hint={t('development.emailHint')}
              field="email"
              error={error}
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
            <button type="submit" disabled={busy}>
              {t('development.submit')}
            </button>
          </form>
        </section>
      )}

      <ErrorAlert error={error} />
    </section>
  )
}
