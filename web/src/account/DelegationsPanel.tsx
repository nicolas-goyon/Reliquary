import { useCallback, useEffect, useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/session'
import { ErrorAlert, SuccessAlert } from '../components/Forms'

/** The scopes of a delegation, in the order of the API. */
const delegationScopes = ['decks:read', 'decks:write', 'collection:read', 'collection:write', 'profile:read'] as const

type DelegationScope = (typeof delegationScopes)[number]

/** Gets the translation key of a scope. i18next reads ":" as the namespace separator, so the keys use "_". */
function scopeKey(scope: DelegationScope) {
  return `delegations.scope.${scope.replace(':', '_') as ScopeKey}` as const
}

type ScopeKey = 'decks_read' | 'decks_write' | 'collection_read' | 'collection_write' | 'profile_read'

/** The durations that the user can choose for a new delegation, in days. */
const durations = [30, 90, 365] as const

interface ServiceAccountSummary {
  id: string
  name: string
  description: string
}

interface Delegation {
  id: string
  serviceAccount: ServiceAccountSummary
  scopes: DelegationScope[]
  createdAt: string
  expiresAt: string
  lastUsedAt: string | null
}

/**
 * The delegations of the user: the modules that can act for the user, with some scopes, until a date.
 * The user gives a delegation to a service account of the instance, and revokes it. See ADR 0037.
 */
export function DelegationsPanel() {
  const { t, i18n } = useTranslation('account')
  const { request } = useAuth()
  const id = useId()
  const [delegations, setDelegations] = useState<Delegation[] | null>(null)
  const [serviceAccounts, setServiceAccounts] = useState<ServiceAccountSummary[]>([])
  const [serviceAccountId, setServiceAccountId] = useState('')
  const [scopes, setScopes] = useState<DelegationScope[]>(['decks:read'])
  const [days, setDays] = useState<number>(90)
  const [created, setCreated] = useState<Delegation | null>(null)
  const [error, setError] = useState<unknown>(null)

  const load = useCallback(() => request<Delegation[]>('/me/delegations').then(setDelegations), [request])

  useEffect(() => {
    load().catch((caught: unknown) => setError(caught))
    request<ServiceAccountSummary[]>('/service-accounts')
      .then((items) => {
        setServiceAccounts(items)
        setServiceAccountId((current) => current || (items[0]?.id ?? ''))
      })
      .catch((caught: unknown) => setError(caught))
  }, [load, request])

  function toggleScope(scope: DelegationScope) {
    setScopes((current) => (current.includes(scope) ? current.filter((item) => item !== scope) : [...current, scope]))
  }

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setCreated(null)
    try {
      setCreated(await request<Delegation>('/me/delegations', { method: 'POST', body: { serviceAccountId, scopes, expiresInDays: days } }))
      await load()
    } catch (caught) {
      setError(caught)
    }
  }

  async function handleRevoke(delegation: Delegation) {
    if (!window.confirm(t('delegations.revokeConfirm', { name: delegation.serviceAccount.name }))) {
      return
    }
    setError(null)
    try {
      await request(`/me/delegations/${delegation.id}`, { method: 'DELETE' })
      if (created?.id === delegation.id) {
        setCreated(null)
      }
      await load()
    } catch (caught) {
      setError(caught)
    }
  }

  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  return (
    <section className="panel" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t('delegations.title')}</h2>
      <p>{t('delegations.description')}</p>
      {created && <SuccessAlert>{t('delegations.created', { name: created.serviceAccount.name, date: dates.format(new Date(created.expiresAt)) })}</SuccessAlert>}
      {delegations && delegations.length === 0 && <p>{t('delegations.none')}</p>}
      {delegations && delegations.length > 0 && (
        <table className="table">
          <thead>
            <tr>
              <th scope="col">{t('delegations.module')}</th>
              <th scope="col">{t('delegations.scopes')}</th>
              <th scope="col">{t('delegations.expiresAt')}</th>
              <th scope="col">{t('delegations.lastUsedAt')}</th>
              <th scope="col">
                <span className="visually-hidden">{t('delegations.actions')}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {delegations.map((delegation) => (
              <tr key={delegation.id}>
                <td>
                  <strong>{delegation.serviceAccount.name}</strong>
                  {delegation.serviceAccount.description && <p className="field-hint">{delegation.serviceAccount.description}</p>}
                </td>
                <td>
                  <ul>
                    {delegation.scopes.map((scope) => (
                      <li key={scope}>{t(scopeKey(scope))}</li>
                    ))}
                  </ul>
                </td>
                <td>{dates.format(new Date(delegation.expiresAt))}</td>
                <td>{delegation.lastUsedAt ? dates.format(new Date(delegation.lastUsedAt)) : t('delegations.neverUsed')}</td>
                <td>
                  <button type="button" className="button-danger" onClick={() => void handleRevoke(delegation)}>
                    {t('delegations.revoke')}
                    <span className="visually-hidden"> {delegation.serviceAccount.name}</span>
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {serviceAccounts.length === 0 ? (
        <p>{t('delegations.noServiceAccount')}</p>
      ) : (
        <form className="form" onSubmit={(event) => void handleCreate(event)} noValidate>
          <h3>{t('delegations.newTitle')}</h3>
          <div className="field">
            <label htmlFor={`${id}-module`}>{t('delegations.module')}</label>
            <select id={`${id}-module`} value={serviceAccountId} onChange={(event) => setServiceAccountId(event.target.value)}>
              {serviceAccounts.map((serviceAccount) => (
                <option key={serviceAccount.id} value={serviceAccount.id}>
                  {serviceAccount.description ? `${serviceAccount.name} (${serviceAccount.description})` : serviceAccount.name}
                </option>
              ))}
            </select>
          </div>
          <fieldset className="filter-group">
            <legend>{t('delegations.scopes')}</legend>
            {delegationScopes.map((scope) => (
              <label key={scope} className="filter-check">
                <input type="checkbox" checked={scopes.includes(scope)} onChange={() => toggleScope(scope)} />
                {t(scopeKey(scope))}
              </label>
            ))}
          </fieldset>
          <div className="field">
            <label htmlFor={`${id}-duration`}>{t('delegations.duration')}</label>
            <select id={`${id}-duration`} value={days} onChange={(event) => setDays(Number(event.target.value))}>
              {durations.map((duration) => (
                <option key={duration} value={duration}>
                  {t('delegations.days', { count: duration })}
                </option>
              ))}
            </select>
          </div>
          <button type="submit" className="button-primary" disabled={scopes.length === 0 || !serviceAccountId}>
            {t('delegations.submit')}
          </button>
        </form>
      )}
      <ErrorAlert error={error} />
    </section>
  )
}
