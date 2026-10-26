import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError, apiRequest } from '../api/client'
import { useAuth } from '../auth/session'
import { ErrorAlert, SuccessAlert, TextField } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate, safeReturnUrl, useLocation } from '../router/navigation'

/** The page of the link in the email of the address check. */
export function CheckEmailPage() {
  const { t } = useTranslation('auth')
  const token = useLocation().searchParams.get('token') ?? ''
  const [state, setState] = useState<'checking' | 'done' | { error: unknown }>('checking')
  const sent = useRef(false)

  useEffect(() => {
    // The token is single-use, so the page sends it one time only, also in the strict mode of React.
    if (sent.current) {
      return
    }
    sent.current = true
    apiRequest('/auth/email-check', { method: 'POST', body: { token } })
      .then(() => setState('done'))
      .catch((error: unknown) => setState({ error }))
  }, [token])

  return (
    <section className="page page-narrow">
      <h1>{t('checkEmail.title')}</h1>
      {state === 'checking' && <p role="status">{t('checkEmail.checking')}</p>}
      {state === 'done' && <SuccessAlert>{t('checkEmail.done')}</SuccessAlert>}
      {typeof state === 'object' && <ErrorAlert error={state.error} />}
      {state !== 'checking' && (
        <p>
          <Link to="/sign-in">{t('checkEmail.signInLink')}</Link>
        </p>
      )}
    </section>
  )
}

/** The page that sends the email of the password reset. */
export function ForgotPasswordPage() {
  const { t } = useTranslation('auth')
  const [email, setEmail] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [done, setDone] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      await apiRequest('/auth/password/forgot', { method: 'POST', body: { email } })
      setDone(true)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <section className="page page-narrow">
      <h1>{t('forgotPassword.title')}</h1>
      {done ? (
        <SuccessAlert>{t('forgotPassword.done', { email })}</SuccessAlert>
      ) : (
        <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
          <p>{t('forgotPassword.instructions')}</p>
          <TextField label={t('fields.email')} field="email" error={error} type="email" autoComplete="email" required value={email} onChange={(event) => setEmail(event.target.value)} />
          <ErrorAlert error={error} />
          <button type="submit" className="button-primary">
            {t('forgotPassword.submit')}
          </button>
        </form>
      )}
      <p>
        <Link to="/sign-in">{t('checkEmail.signInLink')}</Link>
      </p>
    </section>
  )
}

/** The page of the link in the email of the password reset. */
export function ResetPasswordPage() {
  const { t } = useTranslation('auth')
  const token = useLocation().searchParams.get('token') ?? ''
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [done, setDone] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      await apiRequest('/auth/password/reset', { method: 'POST', body: { token, password } })
      setDone(true)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <section className="page page-narrow">
      <h1>{t('resetPassword.title')}</h1>
      {done ? (
        <>
          <SuccessAlert>{t('resetPassword.done')}</SuccessAlert>
          <p>
            <Link to="/sign-in">{t('checkEmail.signInLink')}</Link>
          </p>
        </>
      ) : (
        <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
          <TextField
            label={t('fields.newPassword')}
            field="password"
            error={error}
            type="password"
            autoComplete="new-password"
            required
            minLength={10}
            hint={t('fields.passwordHint')}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
          <ErrorAlert error={error} />
          <button type="submit" className="button-primary">
            {t('resetPassword.submit')}
          </button>
        </form>
      )}
    </section>
  )
}

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
            <Link to="/sign-in">{t('checkEmail.signInLink')}</Link>
          </p>
        </>
      ) : (
        <p role="status">{t('authComplete.waiting')}</p>
      )}
    </section>
  )
}
