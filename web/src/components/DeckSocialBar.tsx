import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Deck } from '../api/decks'
import type { PublicDeckSocial } from '../api/publicDecks'
import { useAuth } from '../auth/session'
import { Link } from '../router/Link'
import { ErrorAlert } from './Forms'
import { ReportButton } from './moderation/ReportDialog'

/**
 * The social bar of a deck: the author, the favorite button, the counts, and the share link of an unlisted or public deck.
 */
export function DeckSocialBar({ deck }: { deck: Deck }) {
  const { t } = useTranslation('decks')
  const { state, request } = useAuth()
  const [social, setSocial] = useState<PublicDeckSocial | null>(null)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (deck.visibility === 'private') {
      return
    }
    request<PublicDeckSocial>(`/public-decks/${deck.id}/social`)
      .then(setSocial)
      .catch(() => setSocial(null))
  }, [deck.id, deck.visibility, deck.version, request])

  if (deck.visibility === 'private') {
    return null
  }

  async function toggleFavorite() {
    if (!social) {
      return
    }
    setError(null)
    try {
      await request(`/public-decks/${deck.id}/favorite`, { method: social.favorite ? 'DELETE' : 'PUT' })
      setSocial({ ...social, favorite: !social.favorite, favoriteCount: social.favoriteCount + (social.favorite ? -1 : 1) })
    } catch (caught) {
      setError(caught)
    }
  }

  async function share() {
    try {
      await navigator.clipboard.writeText(`${window.location.origin}/decks/${deck.id}`)
      setCopied(true)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <div className="deck-social">
      {social?.author && !deck.isOwner && (
        <span>
          {t('explore.by')} <Link to={`/authors/${social.author.id}`}>{social.author.displayName}</Link>
        </span>
      )}
      {social?.public && (
        <>
          <span>{t('explore.favorites', { count: social.favoriteCount })}</span>
          <span>{t('explore.copies', { count: social.copyCount })}</span>
          {state.status === 'authenticated' && !deck.isOwner && (
            <button type="button" aria-pressed={social.favorite} onClick={() => void toggleFavorite()}>
              {social.favorite ? t('social.unfavorite') : t('social.favorite')}
            </button>
          )}
          {!deck.isOwner && <ReportButton itemType="deck" itemId={deck.id} />}
        </>
      )}
      <button type="button" onClick={() => void share()}>
        {t('social.share')}
      </button>
      {copied && (
        <span role="status" className="field-hint">
          {t('social.copied')}
        </span>
      )}
      <ErrorAlert error={error} />
    </div>
  )
}
