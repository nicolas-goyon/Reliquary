import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { apiRequest } from '../api/client'
import { formatKey, lineImage, type Deck } from '../api/decks'
import type { PublicDeck } from '../api/publicDecks'
import { imageGroups, isZoneGroup, type ImageGroup } from './deckImageLayout'
import { ManaCost } from '../components/ManaCost'
import './embed.css'

type Loaded = { state: 'loading' } | { state: 'missing' } | { state: 'done'; deck: Deck; summary: PublicDeck }

/**
 * The embed page of a public deck: a read-only list with minimal chrome, for an iframe on another site.
 * The page sends no cookie and no token. It shows only a deck of the read model of the public decks.
 * See docs/decisions/0046-deck-image-and-embed.md.
 */
export function EmbedDeckPage({ id }: { id: string }) {
  const { t } = useTranslation('decks')
  const [loaded, setLoaded] = useState<Loaded>({ state: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    // The public deck first: an unlisted deck has a deck page, but it must not show on other sites.
    apiRequest<PublicDeck>(`/public-decks/${id}`, { signal: controller.signal })
      .then(async (summary) => setLoaded({ state: 'done', summary, deck: await apiRequest<Deck>(`/decks/${id}`, { signal: controller.signal }) }))
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setLoaded({ state: 'missing' })
        }
      })
    return () => controller.abort()
  }, [id])

  if (loaded.state === 'loading') {
    return (
      <main className="embed">
        <p role="status">{t('editor.loading')}</p>
      </main>
    )
  }
  if (loaded.state === 'missing') {
    return (
      <main className="embed">
        <h1>{t('embed.missingTitle')}</h1>
        <p>{t('embed.missing')}</p>
      </main>
    )
  }

  const { deck, summary } = loaded
  const deckUrl = `${window.location.origin}/decks/${deck.id}`
  const commander = deck.lines.find((line) => line.zone === 'commander')
  const art = commander && lineImage(deck, commander)
  return (
    <main className="embed">
      <header className="embed-header">
        {art && <img className="embed-commander" src={art} alt={deck.cards[commander.oracleId]?.name ?? ''} />}
        <div>
          <h1>
            <a href={deckUrl} target="_blank" rel="noopener noreferrer">
              {deck.name}
            </a>
          </h1>
          <p className="embed-meta">
            {t(`formats.${formatKey(deck.format)}`)} · {t('editor.cardCount', { count: summary.cardCount })} · {t('share.byAuthor', { name: summary.author.displayName })}
          </p>
        </div>
      </header>
      <div className="embed-groups">
        {imageGroups(deck).map((group) => (
          <EmbedGroup key={group.key} deck={deck} group={group} />
        ))}
      </div>
      <footer className="embed-footer">
        <a href={deckUrl} target="_blank" rel="noopener noreferrer">
          {t('embed.open')}
        </a>
      </footer>
    </main>
  )
}

function EmbedGroup({ deck, group }: { deck: Deck; group: ImageGroup }) {
  const { t } = useTranslation('decks')
  const name = isZoneGroup(group.key) ? t(`zones.${group.key}`) : t(`analysis.types.keys.${group.key}`)
  return (
    <section className="embed-group" aria-label={name}>
      <h2>{t('share.groupTitle', { name, count: group.count })}</h2>
      <ul>
        {group.lines.map((line, index) => (
          <li key={`${line.oracleId}-${index}`}>
            <span className="embed-quantity">{line.quantity}</span> <span className="embed-name">{line.name}</span> <ManaCost cost={deck.cards[line.oracleId]?.manaCost} />
          </li>
        ))}
      </ul>
    </section>
  )
}
