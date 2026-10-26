import { useContext } from 'react'
import { useTranslation } from 'react-i18next'
import type { Account } from '../api/types'
import { AuthContext } from '../auth/session'
import { isSupportedLanguage, supportedLanguages } from '../i18n'

/** Lets the user choose the interface language. The choice stays in the browser and, for a signed-in user, in the account. */
export function LanguageSwitcher() {
  const { t, i18n } = useTranslation()
  const auth = useContext(AuthContext)
  const current = isSupportedLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : 'en'

  function change(language: string) {
    void i18n.changeLanguage(language)
    if (auth?.state.status === 'authenticated' && isSupportedLanguage(language) && auth.state.account.language !== language) {
      auth
        .request<Account>('/me', { method: 'PATCH', body: { language } })
        .then((account) => auth.updateAccount(account))
        .catch(() => undefined)
    }
  }

  return (
    <label className="language-switcher">
      <span>{t('language.label')}</span>
      <select value={current} onChange={(event) => change(event.target.value)}>
        {supportedLanguages.map((language) => (
          <option key={language} value={language} lang={language}>
            {t(`language.${language}`)}
          </option>
        ))}
      </select>
    </label>
  )
}
