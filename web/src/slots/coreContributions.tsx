import { AccountMenu, MainNavigation } from '../components/Navigation'
import { ApiKeysPanel, LoginsPanel, PasswordPanel, ProfilePanel } from '../account/AccountPanels'
import { AccountDeletionPanel, DataExportPanel } from '../account/PersonalDataPanels'
import { CommanderPanel, CurvePanel, DistributionPanel, DrawsPanel, ManaPanel } from '../components/analysis/AnalysisPanels'
import i18n from '../i18n'
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
  registry.register('account.page', { id: 'core.dataExport', order: 50, render: () => <DataExportPanel /> })
  registry.register('account.page', { id: 'core.deletion', order: 60, render: () => <AccountDeletionPanel /> })

  // The tabs of the deck analysis. The tab list calls the titles in each render, so the titles follow the language.
  registry.register('analysis.tabs', { id: 'core.curve', order: 10, title: () => i18n.t('decks:analysis.tabs.curve'), render: (context) => <CurvePanel {...context} /> })
  registry.register('analysis.tabs', { id: 'core.distribution', order: 20, title: () => i18n.t('decks:analysis.tabs.distribution'), render: (context) => <DistributionPanel {...context} /> })
  registry.register('analysis.tabs', { id: 'core.mana', order: 30, title: () => i18n.t('decks:analysis.tabs.mana'), render: (context) => <ManaPanel {...context} /> })
  registry.register('analysis.tabs', { id: 'core.draws', order: 40, title: () => i18n.t('decks:analysis.tabs.draws'), render: (context) => <DrawsPanel {...context} /> })
  registry.register('analysis.tabs', {
    id: 'core.commander',
    order: 50,
    title: ({ analysis }) => (analysis.needs || analysis.bracket ? i18n.t('decks:analysis.tabs.commander') : null),
    render: (context) => <CommanderPanel {...context} />,
  })
}
