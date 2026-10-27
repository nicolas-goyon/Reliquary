import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Page } from '../api/cards'
import type { Author, PublicDeck } from '../api/publicDecks'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { PublicDeckList } from '../components/PublicDeckList'

/** The profile of an author: name, counts, the follow button and the public decks. */
export function AuthorPage({ id }: { id: string }) {
  const { t } = useTranslation('decks')
  const { state, request } = useAuth()
  const [author, setAuthor] = useState<Author | null>(null)
  const [decks, setDecks] = useState<PublicDeck[]>([])
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (state.status === 'loading') {
      return
    }
    Promise.all([request<Author>(`/authors/${id}`), request<Page<PublicDeck>>(`/public-decks?author=${id}&limit=50`)])
      .then(([profile, page]) => {
        setAuthor(profile)
        setDecks(page.items)
      })
      .catch((caught: unknown) => setError(caught))
  }, [id, request, state.status])

  async function toggleFollow() {
    if (!author) {
      return
    }
    setError(null)
    try {
      await request(`/authors/${id}/follow`, { method: author.following ? 'DELETE' : 'PUT' })
      setAuthor({ ...author, following: !author.following, followerCount: author.followerCount + (author.following ? -1 : 1) })
    } catch (caught) {
      setError(caught)
    }
  }

  if (!author) {
    return <ErrorAlert error={error} />
  }

  const isSelf = state.status === 'authenticated' && state.account.id === author.id
  return (
    <section className="page page-wide">
      <div className="page-header">
        <h1>{author.displayName}</h1>
        {state.status === 'authenticated' && !isSelf && (
          <button type="button" className={author.following ? undefined : 'button-primary'} aria-pressed={author.following} onClick={() => void toggleFollow()}>
            {author.following ? t('author.unfollow') : t('author.follow')}
          </button>
        )}
      </div>
      <p>
        {t('author.decks', { count: author.deckCount })} · {t('author.followers', { count: author.followerCount })}
      </p>
      <ErrorAlert error={error} />
      <h2>{t('author.publicDecks')}</h2>
      <PublicDeckList decks={decks} showAuthor={false} />
    </section>
  )
}
