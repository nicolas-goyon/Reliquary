import { useTranslation } from 'react-i18next'
import { isSupportedLanguage, supportedLanguages } from '../i18n'

/** Lets the user choose the interface language. The choice stays in the browser. */
export function LanguageSwitcher() {
  const { t, i18n } = useTranslation()
  const current = isSupportedLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : 'en'

  return (
    <label className="language-switcher">
      <span>{t('language.label')}</span>
      <select value={current} onChange={(event) => void i18n.changeLanguage(event.target.value)}>
        {supportedLanguages.map((language) => (
          <option key={language} value={language} lang={language}>
            {t(`language.${language}`)}
          </option>
        ))}
      </select>
    </label>
  )
}
