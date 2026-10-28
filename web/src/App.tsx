import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { AccountPage } from './pages/AccountPage'
import { CardPage } from './pages/CardPage'
import { AuthorPage } from './pages/AuthorPage'
import { CardSearchPage } from './pages/CardSearchPage'
import { ComparePage } from './pages/ComparePage'
import { CollectionPage } from './pages/CollectionPage'
import { DeckImportPage } from './pages/DeckImportPage'
import { DeckListPage } from './pages/DeckListPage'
import { DeckPage } from './pages/DeckPage'
import { AuthCompletePage, CheckEmailPage, ForgotPasswordPage, ResetPasswordPage } from './pages/EmailLinkPages'
import { ExplorePage } from './pages/ExplorePage'
import { HomePage, NotFoundPage } from './pages/HomePage'
import { ModerationPage } from './pages/ModerationPage'
import { InventoryPage } from './pages/InventoryPage'
import { LocationPage } from './pages/LocationPage'
import { LocationsPage } from './pages/LocationsPage'
import { MovementsPage } from './pages/MovementsPage'
import { RegisterPage } from './pages/RegisterPage'
import { SignInPage } from './pages/SignInPage'
import { AuthProvider } from './auth/AuthProvider'
import { useAuth } from './auth/session'
import { LanguageSwitcher } from './components/LanguageSwitcher'
import { ShortcutHelp } from './components/ShortcutHelp'
import { ThemeSwitcher } from './components/ThemeSwitcher'
import { UpdatePrompt } from './components/UpdatePrompt'
import { useLocation } from './router/navigation'
import { usePageTitle } from './router/pageTitle'
import { shortcutElement, takePendingFocus, useShortcuts } from './shortcuts/shortcuts'
import { SlotRegistry, SlotRegistryContext } from './slots/registry'
import { Slot } from './slots/Slot'
import { registerCoreContributions } from './slots/coreContributions'

const repositoryUrl = 'https://github.com/nicolas-goyon/reliquary'

/** The pages with a parameter in their path. */
const patternRoutes: { pattern: RegExp; render(match: RegExpMatchArray): ReactNode }[] = [
  { pattern: /^\/cards\/([0-9a-f-]{36})$/, render: (match) => <CardPage key={match[1]} id={match[1]} /> },
  { pattern: /^\/decks\/([0-9a-f-]{36})$/, render: (match) => <DeckPage key={match[1]} id={match[1]} /> },
  { pattern: /^\/authors\/([0-9a-f-]{36})$/, render: (match) => <AuthorPage key={match[1]} id={match[1]} /> },
  { pattern: /^\/collection\/locations\/([0-9a-f-]{36})$/, render: (match) => <LocationPage key={match[1]} id={match[1]} /> },
  { pattern: /^\/collection\/locations\/([0-9a-f-]{36})\/inventory$/, render: (match) => <InventoryPage key={match[1]} id={match[1]} /> },
]

const routes: Record<string, () => ReactNode> = {
  '/cards': () => <CardSearchPage />,
  '/decks': () => <DeckListPage />,
  '/decks/import': () => <DeckImportPage />,
  '/compare': () => <ComparePage />,
  '/explore': () => <ExplorePage />,
  '/collection': () => <CollectionPage />,
  '/collection/locations': () => <LocationsPage />,
  '/collection/movements': () => <MovementsPage />,
  '/': () => <HomePage />,
  '/sign-in': () => <SignInPage />,
  '/register': () => <RegisterPage />,
  '/check-email': () => <CheckEmailPage />,
  '/forgot-password': () => <ForgotPasswordPage />,
  '/reset-password': () => <ResetPasswordPage />,
  '/auth/complete': () => <AuthCompletePage />,
  '/account': () => <AccountPage />,
  '/admin/moderation': () => <ModerationPage />,
}

function patternPage(path: string): () => ReactNode {
  for (const route of patternRoutes) {
    const match = path.match(route.pattern)
    if (match) {
      return () => route.render(match)
    }
  }
  return () => <NotFoundPage />
}

function createCoreRegistry(): SlotRegistry {
  const registry = new SlotRegistry()
  registerCoreContributions(registry)
  return registry
}

/** The application: the providers, the shell and the pages. */
export function App({ registry }: { registry?: SlotRegistry }) {
  const [slotRegistry] = useState(() => registry ?? createCoreRegistry())

  return (
    <SlotRegistryContext.Provider value={slotRegistry}>
      <AuthProvider>
        <Shell />
      </AuthProvider>
    </SlotRegistryContext.Provider>
  )
}

function Shell() {
  const { t } = useTranslation()
  const { state } = useAuth()
  const location = useLocation()
  const account = state.status === 'authenticated' ? state.account : null
  const page = routes[location.pathname] ?? patternPage(location.pathname)
  const main = useRef<HTMLElement>(null)
  usePageTitle(main, t('app.name'))
  const [helpOpen, setHelpOpen] = useState(false)
  const openHelp = useCallback(() => setHelpOpen(true), [setHelpOpen])
  useShortcuts(!helpOpen, openHelp)

  // Move the focus to the new page, so a screen reader announces it.
  // A shortcut can ask for a field of the new page, for example "/" for the card search.
  useEffect(() => {
    main.current?.focus({ preventScroll: true })
    const target = takePendingFocus()
    if (target) {
      shortcutElement(target)?.focus()
    }
  }, [location.pathname])

  return (
    <div className="app">
      <a className="skip-link" href="#main">
        {t('navigation.skipToContent')}
      </a>
      <header className="app-header">
        <span className="app-name">{t('app.name')}</span>
        <nav aria-label={t('navigation.label')}>
          <Slot name="navigation" context={{ account }} className="slot-inline" />
        </nav>
        <div className="app-header-end">
          <Slot name="app.menu" context={{ account }} className="slot-inline" />
          <LanguageSwitcher />
          <ThemeSwitcher />
        </div>
      </header>
      <UpdatePrompt />

      <main className="app-main" id="main" tabIndex={-1} ref={main}>
        {page()}
      </main>

      <footer className="app-footer">
        <p>
          {t('footer.license')} <a href={repositoryUrl}>{t('footer.source')}</a>
        </p>
        <button type="button" className="button-link" onClick={openHelp}>
          {t('shortcuts.open')}
        </button>
      </footer>
      {helpOpen && <ShortcutHelp onClose={() => setHelpOpen(false)} />}
    </div>
  )
}
