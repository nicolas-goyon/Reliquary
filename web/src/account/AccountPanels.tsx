import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Account, ApiKey, CreatedApiKey, MeResponse, SessionResponse } from '../api/types'
import { useAuth } from '../auth/session'
import { ErrorAlert, SuccessAlert, TextField } from '../components/Forms'
import { isSupportedLanguage, supportedLanguages } from '../i18n'

/** The display name and the interface language of the account. */
export function ProfilePanel({ account }: { account: Account }) {
  const { t, i18n } = useTranslation('account')
  const { t: tCommon } = useTranslation()
  const { request, updateAccount } = useAuth()
  const [displayName, setDisplayName] = useState(account.displayName)
  const [language, setLanguage] = useState(account.language)
  const [error, setError] = useState<unknown>(null)
  const [saved, setSaved] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setSaved(false)
    try {
      const updated = await request<Account>('/me', { method: 'PATCH', body: { displayName, language } })
      updateAccount(updated)
      if (isSupportedLanguage(updated.language)) {
        await i18n.changeLanguage(updated.language)
      }
      setSaved(true)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <section className="panel" aria-labelledby="profile-title">
      <h2 id="profile-title">{t('profile.title')}</h2>
      <dl className="facts">
        <dt>{t('profile.email')}</dt>
        <dd>{account.email}</dd>
      </dl>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <TextField label={t('profile.displayName')} field="displayName" error={error} maxLength={50} value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
        <div className="field">
          <label htmlFor="profile-language">{t('profile.language')}</label>
          <select id="profile-language" value={language} onChange={(event) => setLanguage(event.target.value as Account['language'])}>
            {supportedLanguages.map((code) => (
              <option key={code} value={code} lang={code}>
                {tCommon(`language.${code}`)}
              </option>
            ))}
          </select>
        </div>
        <ErrorAlert error={error} />
        {saved && <SuccessAlert>{t('profile.saved')}</SuccessAlert>}
        <button type="submit" className="button-primary">
          {t('profile.submit')}
        </button>
      </form>
    </section>
  )
}

/** The change of the password. */
export function PasswordPanel({ account }: { account: Account }) {
  const { t } = useTranslation('account')
  const { t: tAuth } = useTranslation('auth')
  const { request, acceptSession } = useAuth()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [saved, setSaved] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setSaved(false)
    try {
      const session = await request<SessionResponse>('/me/password', {
        method: 'POST',
        body: { currentPassword: account.hasPassword ? currentPassword : null, newPassword },
      })
      acceptSession(session.accessToken, session.expiresIn, session.account)
      setCurrentPassword('')
      setNewPassword('')
      setSaved(true)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <section className="panel" aria-labelledby="password-title">
      <h2 id="password-title">{account.hasPassword ? t('password.title') : t('password.titleFirst')}</h2>
      <p>{t('password.description')}</p>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        {account.hasPassword && (
          <TextField
            label={t('password.current')}
            type="password"
            autoComplete="current-password"
            value={currentPassword}
            onChange={(event) => setCurrentPassword(event.target.value)}
          />
        )}
        <TextField
          label={t('password.new')}
          field="newPassword"
          error={error}
          type="password"
          autoComplete="new-password"
          minLength={10}
          hint={tAuth('fields.passwordHint')}
          value={newPassword}
          onChange={(event) => setNewPassword(event.target.value)}
        />
        <ErrorAlert error={error} />
        {saved && <SuccessAlert>{t('password.saved')}</SuccessAlert>}
        <button type="submit" className="button-primary">
          {t('password.submit')}
        </button>
      </form>
    </section>
  )
}

/** The personal API keys of the account. */
export function ApiKeysPanel() {
  const { t, i18n } = useTranslation('account')
  const { request } = useAuth()
  const [keys, setKeys] = useState<ApiKey[] | null>(null)
  const [name, setName] = useState('')
  const [created, setCreated] = useState<CreatedApiKey | null>(null)
  const [error, setError] = useState<unknown>(null)

  const load = useCallback(async () => {
    try {
      setKeys(await request<ApiKey[]>('/me/api-keys'))
    } catch (caught) {
      setError(caught)
    }
  }, [request])

  useEffect(() => {
    request<ApiKey[]>('/me/api-keys')
      .then(setKeys)
      .catch((caught: unknown) => setError(caught))
  }, [request])

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    setError(null)
    try {
      setCreated(await request<CreatedApiKey>('/me/api-keys', { method: 'POST', body: { name } }))
      setName('')
      await load()
    } catch (caught) {
      setError(caught)
    }
  }

  async function handleRevoke(key: ApiKey) {
    if (!window.confirm(t('apiKeys.revokeConfirm', { name: key.name }))) {
      return
    }
    setError(null)
    try {
      await request(`/me/api-keys/${key.id}`, { method: 'DELETE' })
      if (created?.id === key.id) {
        setCreated(null)
      }
      await load()
    } catch (caught) {
      setError(caught)
    }
  }

  const dateFormat = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })

  return (
    <section className="panel" aria-labelledby="api-keys-title">
      <h2 id="api-keys-title">{t('apiKeys.title')}</h2>
      <p>{t('apiKeys.description')}</p>
      {created && (
        <div className="alert alert-success" role="status">
          <p>{t('apiKeys.createdOnce', { name: created.name })}</p>
          <code className="secret">{created.key}</code>
        </div>
      )}
      {keys && keys.length === 0 && <p>{t('apiKeys.none')}</p>}
      {keys && keys.length > 0 && (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t('apiKeys.name')}</th>
                <th scope="col">{t('apiKeys.prefix')}</th>
                <th scope="col">{t('apiKeys.createdAt')}</th>
                <th scope="col">{t('apiKeys.lastUsedAt')}</th>
                <th scope="col">
                  <span className="visually-hidden">{t('apiKeys.actions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {keys.map((key) => (
                <tr key={key.id}>
                  <td>{key.name}</td>
                  <td>
                    <code>rlq_{key.prefix}_…</code>
                  </td>
                  <td>{dateFormat.format(new Date(key.createdAt))}</td>
                  <td>{key.lastUsedAt ? dateFormat.format(new Date(key.lastUsedAt)) : t('apiKeys.neverUsed')}</td>
                  <td>
                    <button type="button" className="button-danger" onClick={() => void handleRevoke(key)}>
                      {t('apiKeys.revoke')}
                      <span className="visually-hidden"> {key.name}</span>
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <form className="form form-inline" onSubmit={(event) => void handleCreate(event)} noValidate>
        <TextField label={t('apiKeys.newName')} field="name" error={error} maxLength={50} value={name} onChange={(event) => setName(event.target.value)} />
        <button type="submit" className="button-primary">
          {t('apiKeys.create')}
        </button>
      </form>
      <ErrorAlert error={error} />
    </section>
  )
}

/** The OAuth logins of the account. */
export function LoginsPanel() {
  const { t } = useTranslation('account')
  const { request } = useAuth()
  const [logins, setLogins] = useState<string[] | null>(null)

  useEffect(() => {
    request<MeResponse>('/me')
      .then((me) => setLogins(me.externalLogins))
      .catch(() => setLogins([]))
  }, [request])

  if (!logins) {
    return null
  }

  return (
    <section className="panel" aria-labelledby="logins-title">
      <h2 id="logins-title">{t('logins.title')}</h2>
      {logins.length === 0 ? (
        <p>{t('logins.none')}</p>
      ) : (
        <ul>
          {logins.map((login) => (
            <li key={login}>{login}</li>
          ))}
        </ul>
      )}
    </section>
  )
}
