import { useTranslation } from 'react-i18next'
import { ApiStatus } from './components/ApiStatus'
import { LanguageSwitcher } from './components/LanguageSwitcher'

const repositoryUrl = 'https://github.com/nicolas-goyon/reliquary'
const roadmapUrl = `${repositoryUrl}/blob/develop/docs/roadmap.md`

export function App() {
  const { t } = useTranslation()

  return (
    <div className="app">
      <header className="app-header">
        <span className="app-name">{t('app.name')}</span>
        <LanguageSwitcher />
      </header>

      <main className="app-main">
        <h1>{t('home.title')}</h1>
        <p className="tagline">{t('app.tagline')}</p>
        <p>{t('home.description')}</p>
        <p>
          <a href={roadmapUrl}>{t('home.roadmapLink')}</a>
        </p>
        <ApiStatus />
      </main>

      <footer className="app-footer">
        <p>
          {t('footer.license')} <a href={repositoryUrl}>{t('footer.source')}</a>
        </p>
      </footer>
    </div>
  )
}
