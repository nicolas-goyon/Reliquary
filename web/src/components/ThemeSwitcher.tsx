import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { applyTheme, isTheme, readStoredTheme, storeTheme, themes, type Theme } from '../theme/theme'

/** Lets the user choose the color theme: the theme of the system, light or dark. The choice stays in the browser. */
export function ThemeSwitcher() {
  const { t } = useTranslation()
  const [theme, setTheme] = useState<Theme>(readStoredTheme)

  function change(value: string) {
    if (!isTheme(value)) {
      return
    }
    setTheme(value)
    storeTheme(value)
    applyTheme(value)
  }

  return (
    <label className="theme-switcher">
      <span>{t('theme.label')}</span>
      <select value={theme} onChange={(event) => change(event.target.value)}>
        {themes.map((value) => (
          <option key={value} value={value}>
            {t(`theme.${value}`)}
          </option>
        ))}
      </select>
    </label>
  )
}
