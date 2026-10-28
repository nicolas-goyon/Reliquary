import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { DeckToken } from '../../api/collection'
import { useAuth } from '../../auth/session'
import { ErrorAlert } from '../Forms'

/** The tokens of an assembled deck location. The owner marks each token as owned or not. */
export function DeckTokensPanel({ locationId }: { locationId: string }) {
  const { t } = useTranslation('collection')
  const { request } = useAuth()
  const [tokens, setTokens] = useState<DeckToken[]>([])
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    request<DeckToken[]>(`/collection/locations/${locationId}/tokens`)
      .then(setTokens)
      .catch((caught: unknown) => setError(caught))
  }, [locationId, request])

  async function toggle(token: DeckToken) {
    setError(null)
    try {
      setTokens(await request<DeckToken[]>(`/collection/locations/${locationId}/tokens/${token.oracleId}`, { method: 'PUT', body: { owned: !token.owned } }))
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <section className="panel" aria-labelledby="deck-tokens-title">
      <h2 id="deck-tokens-title">{t('tokens.title')}</h2>
      <ErrorAlert error={error} />
      {tokens.length === 0 ? (
        <p>{t('tokens.empty')}</p>
      ) : (
        <ul className="inline-list">
          {tokens.map((token) => (
            <li key={token.oracleId}>
              <label className="filter-check">
                <input type="checkbox" checked={token.owned} onChange={() => void toggle(token)} aria-describedby={`token-${token.oracleId}`} />
                <span id={`token-${token.oracleId}`}>{token.name}</span>
                <span className="visually-hidden">{t('tokens.owned')}</span>
              </label>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
