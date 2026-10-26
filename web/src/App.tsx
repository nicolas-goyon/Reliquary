import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { AccountPage } from './pages/AccountPage'
import { AuthCompletePage, CheckEmailPage, ForgotPasswordPage, ResetPasswordPage } from './pages/EmailLinkPages'
import { HomePage, NotFoundPage } from './pages/HomePage'
import { RegisterPage } from './pages/RegisterPage'
import { SignInPage } from './pages/SignInPage'
import { AuthProvider } from './auth/AuthProvider'
import { useAuth } from './auth/session'
import { LanguageSwitcher } from './components/LanguageSwitcher'
import { useLocation } from './router/navigation'
import { SlotRegistry, SlotRegistryContext } from './slots/registry'
import { Slot } from './slots/Slot'
import { registerCoreContributions } from './slots/coreContributions'

const repositoryUrl = 'https://github.com/nicolas-goyon/reliquary'

const routes: Record<string, () => ReactNode> = {
  '/': () => <HomePage />,
  '/sign-in': () => <SignInPage />,
  '/register': () => <RegisterPage />,
  '/check-email': () => <CheckEmailPage />,
  '/forgot-password': () => <ForgotPasswordPage />,
  '/reset-password': () => <ResetPasswordPage />,
  '/auth/complete': () => <AuthCompletePage />,
  '/account': () => <AccountPage />,
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
  const page = routes[location.pathname] ?? (() => <NotFoundPage />)
  const main = useRef<HTMLElement>(null)

  // Move the focus to the new page, so a screen reader announces it.
  useEffect(() => {
    main.current?.focus({ preventScroll: true })
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
        </div>
      </header>

      <main className="app-main" id="main" tabIndex={-1} ref={main}>
        {page()}
      </main>

      <footer className="app-footer">
        <p>
          {t('footer.license')} <a href={repositoryUrl}>{t('footer.source')}</a>
        </p>
      </footer>
    </div>
  )
}
