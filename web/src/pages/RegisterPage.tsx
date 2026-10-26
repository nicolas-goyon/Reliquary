import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { apiRequest } from '../api/client'
import { ErrorAlert, SuccessAlert, TextField } from '../components/Forms'
import { isSupportedLanguage, supportedLanguages } from '../i18n'
import { Link } from '../router/Link'

/** The registration page. */
export function RegisterPage() {
  const { t, i18n } = useTranslation('auth')
  const { t: tCommon } = useTranslation()
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [language, setLanguage] = useState(isSupportedLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : 'en')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await apiRequest('/auth/register', { method: 'POST', body: { email, displayName, password, language } })
      setDone(true)
    } catch (caught) {
      setError(caught)
    } finally {
      setBusy(false)
    }
  }

  if (done) {
    return (
      <section className="page page-narrow">
        <h1>{t('register.title')}</h1>
        <SuccessAlert>{t('register.checkYourEmail', { email })}</SuccessAlert>
        <p>
          <Link to="/sign-in">{t('register.signInLink')}</Link>
        </p>
      </section>
    )
  }

  return (
    <section className="page page-narrow">
      <h1>{t('register.title')}</h1>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <TextField label={t('fields.email')} field="email" error={error} type="email" autoComplete="email" required value={email} onChange={(event) => setEmail(event.target.value)} />
        <TextField
          label={t('fields.displayName')}
          field="displayName"
          error={error}
          autoComplete="nickname"
          required
          maxLength={50}
          hint={t('register.displayNameHint')}
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
        />
        <TextField
          label={t('fields.password')}
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
        <div className="field">
          <label htmlFor="register-language">{t('fields.language')}</label>
          <select id="register-language" value={language} onChange={(event) => setLanguage(event.target.value as typeof language)}>
            {supportedLanguages.map((code) => (
              <option key={code} value={code} lang={code}>
                {tCommon(`language.${code}`)}
              </option>
            ))}
          </select>
        </div>
        <ErrorAlert error={error} />
        <button type="submit" className="button-primary" disabled={busy}>
          {t('register.submit')}
        </button>
      </form>
      <p>
        {t('register.haveAccount')} <Link to="/sign-in">{t('register.signInLink')}</Link>
      </p>
    </section>
  )
}
