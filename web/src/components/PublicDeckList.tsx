import { useTranslation } from 'react-i18next'
import { formatKey } from '../api/decks'
import type { PublicDeck } from '../api/publicDecks'
import { Link } from '../router/Link'
import { CardImage } from './CardImage'
import { ManaCost } from './ManaCost'

/** A list of public decks: commander image, name, format, colors, author and counts. */
export function PublicDeckList({ decks, showAuthor = true }: { decks: PublicDeck[]; showAuthor?: boolean }) {
  const { t, i18n } = useTranslation('decks')
  const dates = new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium' })
  if (decks.length === 0) {
    return <p>{t('explore.empty')}</p>
  }
  return (
    <ul className="public-decks">
      {decks.map((deck) => (
        <li key={deck.id} className="public-deck panel">
          {deck.commanders[0] && <CardImage src={deck.commanders[0].imageUri ?? undefined} alt={deck.commanders[0].name} className="public-deck-image" />}
          <div className="public-deck-body">
            <h3>
              <Link to={`/decks/${deck.id}`}>{deck.name}</Link>
            </h3>
            <p className="public-deck-meta">
              <span>{t(`formats.${formatKey(deck.format)}`)}</span>
              {deck.colorIdentity ? (
                <ManaCost cost={deck.colorIdentity.split('').map((color) => `{${color}}`).join('')} />
              ) : (
                <span>{t('list.colorless')}</span>
              )}
              {deck.commanders.length > 0 && <span>{deck.commanders.map((commander) => commander.name).join(' + ')}</span>}
            </p>
            <p className="public-deck-meta">
              {showAuthor && (
                <span>
                  {t('explore.by')} <Link to={`/authors/${deck.author.id}`}>{deck.author.displayName}</Link>
                </span>
              )}
              <span>{t('explore.cards', { count: deck.cardCount })}</span>
              <span>{t('explore.copies', { count: deck.copyCount })}</span>
              <span>{t('explore.favorites', { count: deck.favoriteCount })}</span>
              <span>{dates.format(new Date(deck.updatedAt))}</span>
            </p>
          </div>
        </li>
      ))}
    </ul>
  )
}
