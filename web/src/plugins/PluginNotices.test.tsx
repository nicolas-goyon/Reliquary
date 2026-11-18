import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import i18n from '../i18n'
import { addNotice, removeNotices } from './notices'
import { PluginNotices } from './PluginNotices'
import { seriousAccessibilityProblems } from '../test/axe'

beforeEach(async () => {
  await i18n.changeLanguage('en')
})

afterEach(() => removeNotices('deck-stats'))

describe('plugin notices', () => {
  it('shows the notice with the name of the plugin, and the user closes it', async () => {
    const { container } = render(<PluginNotices />)

    act(() => addNotice({ pluginId: 'deck-stats', pluginName: 'Deck statistics', development: true, message: '<b>The sync completed.</b>', tone: 'success' }))

    // The core shows the text of the plugin as text, not as HTML.
    expect(screen.getByText('<b>The sync completed.</b>')).toBeInTheDocument()
    expect(screen.getByText('Deck statistics (not checked):')).toBeInTheDocument()
    expect(await seriousAccessibilityProblems(container)).toEqual([])

    await userEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(screen.queryByText('<b>The sync completed.</b>')).not.toBeInTheDocument()
  })
})
