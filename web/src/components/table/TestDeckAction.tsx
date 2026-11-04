import { useTranslation } from 'react-i18next'
import type { Deck } from '../../api/decks'
import { Link } from '../../router/Link'

/** The "Test the deck" link of the deck actions. Each user who can see the deck can use it. */
export function TestDeckAction({ deck }: { deck: Deck }) {
  const { t } = useTranslation('playtest')
  return (
    <Link to={`/decks/${deck.id}/test`} className="button">
      {t('action')}
    </Link>
  )
}
