import { useTranslation } from 'react-i18next'
import { ApiStatus } from '../components/ApiStatus'

const roadmapUrl = 'https://github.com/nicolas-goyon/reliquary/blob/develop/docs/roadmap.md'

/** The home page. */
export function HomePage() {
  const { t } = useTranslation()
  return (
    <section className="page">
      <h1>{t('home.title')}</h1>
      <p className="tagline">{t('app.tagline')}</p>
      <p>{t('home.description')}</p>
      <p>
        <a href={roadmapUrl}>{t('home.roadmapLink')}</a>
      </p>
      <ApiStatus />
    </section>
  )
}

/** The page for an unknown address. */
export function NotFoundPage() {
  const { t } = useTranslation()
  return (
    <section className="page">
      <h1>{t('notFound.title')}</h1>
      <p>{t('notFound.description')}</p>
    </section>
  )
}
