import { afterEach, describe, expect, it, vi } from 'vitest'

// Each test loads a new copy of the i18n module, as at the start of the app.
async function startI18n() {
  vi.resetModules()
  const module = await import('./index')
  return module
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('i18n start', () => {
  it('uses the browser language, then keeps it with the existing storage key', async () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['de-DE', 'fr-CA', 'en'])
    const { default: i18n, languageStorageKey } = await startI18n()
    expect(languageStorageKey).toBe('reliquary.language')
    expect(i18n.resolvedLanguage).toBe('fr')
    expect(localStorage.getItem('reliquary.language')).toBe('fr')
    expect(document.documentElement.lang).toBe('fr')
  })

  it('uses the stored language before the browser language', async () => {
    localStorage.setItem('reliquary.language', 'fr')
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['en-US'])
    const { default: i18n } = await startI18n()
    expect(i18n.resolvedLanguage).toBe('fr')
  })

  it('keeps the language that the user chooses', async () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['en-US'])
    const { default: i18n } = await startI18n()
    await i18n.changeLanguage('fr')
    expect(localStorage.getItem('reliquary.language')).toBe('fr')
    await i18n.changeLanguage('en')
  })
})
