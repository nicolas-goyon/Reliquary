import { useCallback, useEffect, useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { ExtensionList } from '../api/extensions'
import { useAuth } from '../auth/session'
import { ErrorAlert, SuccessAlert, TextField } from '../components/Forms'
import { linkPath, siteKey, type LinkAccountBody, type LinkedAccountList, type LinkedAccountSite } from './api'

/** Gets the name of the plugin in the language of the interface, from the translations of the plugin manifest. */
function pluginName(item: LinkedAccountSite, extensions: ExtensionList | null, language: string | undefined): string {
  const i18n = extensions?.items.find((extension) => extension.id === item.pluginId)?.i18n
  const translated = language ? (i18n?.[language]?.name ?? i18n?.[language.split('-')[0]]?.name) : undefined
  return translated ?? item.pluginName ?? item.pluginId
}

/**
 * The linked accounts of the user: the external sites of the active plugins, with the link and unlink buttons.
 * The core builds the panel from the field `accounts` of the plugin manifests. The panel never shows a secret.
 * See docs/decisions/0051-linked-accounts.md.
 */
export function LinkedAccountsPanel() {
  const { t, i18n } = useTranslation('account')
  const { request } = useAuth()
  const id = useId()
  const [items, setItems] = useState<LinkedAccountSite[] | null>(null)
  const [extensions, setExtensions] = useState<ExtensionList | null>(null)
  const [editing, setEditing] = useState<LinkedAccountSite | null>(null)
  const [userName, setUserName] = useState('')
  const [secret, setSecret] = useState('')
  const [saved, setSaved] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)

  const load = useCallback(() => request<LinkedAccountList>('/me/linked-accounts').then((list) => setItems(list.items)), [request])

  useEffect(() => {
    load().catch((caught: unknown) => setError(caught))
    request<ExtensionList>('/extensions')
      .then(setExtensions)
      .catch(() => setExtensions(null))
  }, [load, request])

  if (!items || items.length === 0) {
    // The panel shows only when a plugin of the user declares a site, or when a link stays.
    return error ? <ErrorAlert error={error} /> : null
  }

  const language = i18n.resolvedLanguage
  const dates = new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' })

  function startLink(item: LinkedAccountSite) {
    setEditing(item)
    setUserName(item.link?.userName ?? '')
    setSecret('')
    setSaved(null)
    setError(null)
  }

  async function handleLink(event: FormEvent) {
    event.preventDefault()
    if (!editing) {
      return
    }
    setError(null)
    const body: LinkAccountBody = editing.auth === 'api-key' ? { secret, userName: userName || undefined } : { userName }
    try {
      await request(linkPath(editing.pluginId, editing.site), { method: 'PUT', body })
      setSaved(t('linkedAccounts.linked', { site: editing.site }))
      setEditing(null)
      setSecret('')
      await load()
    } catch (caught) {
      setError(caught)
    }
  }

  async function handleUnlink(item: LinkedAccountSite) {
    if (!window.confirm(t('linkedAccounts.unlinkConfirm', { site: item.site }))) {
      return
    }
    setError(null)
    setSaved(null)
    try {
      await request(linkPath(item.pluginId, item.site), { method: 'DELETE' })
      if (editing && siteKey(editing) === siteKey(item)) {
        setEditing(null)
      }
      await load()
    } catch (caught) {
      setError(caught)
    }
  }

  function stateText(item: LinkedAccountSite): string {
    if (!item.link) {
      return t('linkedAccounts.state.notLinked')
    }
    if (!item.link.secretReadable) {
      return t('linkedAccounts.state.keyLost')
    }
    return t(`linkedAccounts.state.${item.link.state}`)
  }

  return (
    <section className="panel" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t('linkedAccounts.title')}</h2>
      <p>{t('linkedAccounts.description')}</p>
      {saved && <SuccessAlert>{saved}</SuccessAlert>}
      <table className="table">
        <thead>
          <tr>
            <th scope="col">{t('linkedAccounts.site')}</th>
            <th scope="col">{t('linkedAccounts.plugin')}</th>
            <th scope="col">{t('linkedAccounts.stateTitle')}</th>
            <th scope="col">{t('linkedAccounts.lastSync')}</th>
            <th scope="col">
              <span className="visually-hidden">{t('linkedAccounts.actions')}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={siteKey(item)}>
              <td>
                <strong>{item.site}</strong>
                {item.link?.userName && <p className="field-hint">{item.link.userName}</p>}
              </td>
              <td>
                {pluginName(item, extensions, language)}
                {!item.pluginActive && <p className="field-hint">{t('linkedAccounts.pluginInactive')}</p>}
              </td>
              <td>
                {stateText(item)}
                {item.link?.state === 'failed' && item.link.lastSyncError && <p className="field-hint">{item.link.lastSyncError}</p>}
              </td>
              <td>{item.link?.lastSyncAt ? dates.format(new Date(item.link.lastSyncAt)) : t('linkedAccounts.never')}</td>
              <td>
                <div className="form-actions">
                  {item.pluginActive && item.auth !== 'oauth2-pkce' && (
                    <button type="button" onClick={() => startLink(item)} aria-expanded={editing ? siteKey(editing) === siteKey(item) : false}>
                      {item.link ? t('linkedAccounts.change') : t('linkedAccounts.link')}
                      {' '}<span className="visually-hidden">{item.site}</span>
                    </button>
                  )}
                  {item.pluginActive && item.auth === 'oauth2-pkce' && !item.link && <span className="field-hint">{t('linkedAccounts.oauthLater')}</span>}
                  {item.link && (
                    <button type="button" className="button-danger" onClick={() => void handleUnlink(item)}>
                      {t('linkedAccounts.unlink')}
                      {' '}<span className="visually-hidden">{item.site}</span>
                    </button>
                  )}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {editing && (
        <form className="form" onSubmit={(event) => void handleLink(event)} noValidate>
          <h3>{t('linkedAccounts.formTitle', { site: editing.site })}</h3>
          {editing.auth === 'api-key' && (
            <TextField
              label={t('linkedAccounts.apiKey')}
              field="secret"
              error={error}
              type="password"
              autoComplete="off"
              spellCheck={false}
              required
              value={secret}
              onChange={(event) => setSecret(event.target.value)}
              hint={t('linkedAccounts.apiKeyHint')}
            />
          )}
          <TextField
            label={editing.auth === 'api-key' ? t('linkedAccounts.userNameOptional') : t('linkedAccounts.userName')}
            field="userName"
            error={error}
            autoComplete="off"
            spellCheck={false}
            required={editing.auth === 'public-username'}
            maxLength={100}
            value={userName}
            onChange={(event) => setUserName(event.target.value)}
            hint={editing.auth === 'public-username' ? t('linkedAccounts.userNameHint') : undefined}
          />
          <div className="form-actions">
            <button type="submit" className="button-primary" disabled={editing.auth === 'api-key' ? !secret.trim() : !userName.trim()}>
              {t('linkedAccounts.submit')}
            </button>
            <button type="button" onClick={() => setEditing(null)}>
              {t('linkedAccounts.cancel')}
            </button>
          </div>
        </form>
      )}
      <ErrorAlert error={error} />
    </section>
  )
}
