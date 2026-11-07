import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import styles from '../styles.css?raw'
import { ThemeSwitcher } from '../components/ThemeSwitcher'
import i18n from '../i18n'
import { cssBlock } from '../test/css'
import { applyTheme, readStoredTheme, storeTheme, themeStorageKey } from './theme'

describe('theme', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    document.documentElement.removeAttribute('data-theme')
  })

  afterEach(() => {
    vi.restoreAllMocks()
    document.documentElement.removeAttribute('data-theme')
  })

  it('applies the theme that the user selects and keeps it in the browser', async () => {
    const user = userEvent.setup()
    render(<ThemeSwitcher />)
    expect(screen.getByLabelText('Theme')).toHaveValue('system')

    await user.selectOptions(screen.getByLabelText('Theme'), 'dark')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
    expect(localStorage.getItem(themeStorageKey)).toBe('dark')

    await user.selectOptions(screen.getByLabelText('Theme'), 'light')
    expect(document.documentElement).toHaveAttribute('data-theme', 'light')

    await user.selectOptions(screen.getByLabelText('Theme'), 'system')
    expect(document.documentElement).not.toHaveAttribute('data-theme')
    expect(localStorage.getItem(themeStorageKey)).toBeNull()
  })

  it('reads the stored theme when the page opens', () => {
    localStorage.setItem(themeStorageKey, 'light')
    render(<ThemeSwitcher />)

    expect(screen.getByLabelText('Theme')).toHaveValue('light')
  })

  it('ignores a value in the storage that is not a theme', () => {
    localStorage.setItem(themeStorageKey, 'purple')

    expect(readStoredTheme()).toBe('system')
  })

  it('continues to work when the storage is not available', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('The storage is blocked.', 'SecurityError')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('The storage is blocked.', 'SecurityError')
    })

    expect(readStoredTheme()).toBe('system')
    expect(() => storeTheme('dark')).not.toThrow()
    applyTheme('dark')
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  })

  it('has the same dark tokens for the dark theme and for the dark system setting', () => {
    const chosen = cssBlock(styles, ":root[data-theme='dark']")
    const system = cssBlock(styles, ":root:not([data-theme='light'])")

    expect(chosen.size).toBeGreaterThan(10)
    expect(system).toEqual(chosen)
  })

  it('gives each light token a dark value', () => {
    const light = [...cssBlock(styles, ':root').keys()].filter((name) => name.startsWith('--color-'))
    const dark = [...cssBlock(styles, ":root[data-theme='dark']").keys()].filter((name) => name.startsWith('--color-'))

    expect(dark.sort()).toEqual(light.sort())
  })
})
