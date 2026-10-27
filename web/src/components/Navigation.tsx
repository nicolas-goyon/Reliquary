import { useTranslation } from 'react-i18next'
import type { Account } from '../api/types'
import { useAuth } from '../auth/session'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

/** The main navigation links. */
export function MainNavigation({ account }: { account: Account | null }) {
  const { t } = useTranslation()
  return (
    <ul className="nav-list">
      <li>
        <Link to="/">{t('navigation.home')}</Link>
      </li>
      <li>
        <Link to="/cards">{t('navigation.cards')}</Link>
      </li>
      {account && (
        <li>
          <Link to="/decks">{t('navigation.decks')}</Link>
        </li>
      )}
      {account && (
        <li>
          <Link to="/account">{t('navigation.account')}</Link>
        </li>
      )}
    </ul>
  )
}

/** The menu of the user: the sign-in links, or the name of the user and the sign-out button. */
export function AccountMenu({ account }: { account: Account | null }) {
  const { t } = useTranslation()
  const { signOut } = useAuth()

  if (!account) {
    return (
      <ul className="nav-list">
        <li>
          <Link to="/sign-in">{t('navigation.signIn')}</Link>
        </li>
        <li>
          <Link to="/register">{t('navigation.register')}</Link>
        </li>
      </ul>
    )
  }

  return (
    <ul className="nav-list">
      <li>
        <Link to="/account">{account.displayName}</Link>
      </li>
      <li>
        <button
          type="button"
          className="button-link"
          onClick={() => {
            void signOut().finally(() => navigate('/'))
          }}
        >
          {t('navigation.signOut')}
        </button>
      </li>
    </ul>
  )
}
