import { AccountMenu, MainNavigation } from '../components/Navigation'
import { ApiKeysPanel, LoginsPanel, PasswordPanel, ProfilePanel } from '../account/AccountPanels'
import type { SlotRegistry } from './registry'

/**
 * Registers the panels of the core in the slots. The core uses the same slots as the extensions,
 * so the slot system works before the extensions arrive.
 */
export function registerCoreContributions(registry: SlotRegistry): void {
  registry.register('navigation', { id: 'core.navigation', order: 10, render: ({ account }) => <MainNavigation account={account} /> })
  registry.register('app.menu', { id: 'core.accountMenu', order: 100, render: ({ account }) => <AccountMenu account={account} /> })
  registry.register('account.page', { id: 'core.profile', order: 10, render: ({ account }) => <ProfilePanel account={account} /> })
  registry.register('account.page', { id: 'core.password', order: 20, render: ({ account }) => <PasswordPanel key={account.id} account={account} /> })
  registry.register('account.page', { id: 'core.logins', order: 30, render: () => <LoginsPanel /> })
  registry.register('account.page', { id: 'core.apiKeys', order: 40, render: () => <ApiKeysPanel /> })
}
