/**
 * The color theme of the interface. See docs/development/web-app.md.
 *
 * The choice stays in the local storage of the browser. The account API has no field for the theme,
 * so the choice does not go to other devices.
 */
export const themes = ['system', 'light', 'dark'] as const
export type Theme = (typeof themes)[number]

/** The local storage key that keeps the theme that the user chose. */
export const themeStorageKey = 'reliquary.theme'

export function isTheme(value: unknown): value is Theme {
  return themes.some((theme) => theme === value)
}

/**
 * Reads the theme from the local storage.
 * @returns The stored theme, or "system" when the storage has no theme or is not available.
 */
export function readStoredTheme(): Theme {
  try {
    const value = window.localStorage.getItem(themeStorageKey)
    return isTheme(value) ? value : 'system'
  } catch {
    return 'system'
  }
}

/** Keeps the theme in the local storage. The function does nothing when the storage is not available. */
export function storeTheme(theme: Theme): void {
  try {
    if (theme === 'system') {
      window.localStorage.removeItem(themeStorageKey)
    } else {
      window.localStorage.setItem(themeStorageKey, theme)
    }
  } catch {
    // A private window or a blocked storage: the theme applies until the page closes.
  }
}

/**
 * Applies the theme to the root element. "light" and "dark" set the attribute data-theme.
 * "system" removes the attribute, so the CSS follows the setting of the system.
 */
export function applyTheme(theme: Theme, root: HTMLElement = document.documentElement): void {
  if (theme === 'system') {
    root.removeAttribute('data-theme')
  } else {
    root.setAttribute('data-theme', theme)
  }
}
