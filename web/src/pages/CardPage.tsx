import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { apiRequest } from '../api/client'
import { imageOf, type CardDetail, type CardFace, type Page, type Printing, type Ruling } from '../api/cards'
import { CardImage } from '../components/CardImage'
import { ErrorAlert } from '../components/Forms'
import { ManaCost } from '../components/ManaCost'
import { Slot } from '../slots/Slot'

/** The formats that the card page shows, in this order. */
const shownFormats = ['commander', 'standard', 'pioneer', 'modern', 'legacy', 'vintage', 'pauper', 'brawl', 'oathbreaker', 'paupercommander'] as const

type Loaded = { state: 'loading' } | { state: 'error'; error: unknown } | { state: 'done'; card: CardDetail; rulings: Ruling[] }

/** The page of a card: its faces, its names, its printings, its legalities and its rulings. */
export function CardPage({ id }: { id: string }) {
  const { t } = useTranslation('cards')
  const [loaded, setLoaded] = useState<Loaded>({ state: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([
      apiRequest<CardDetail>(`/cards/${id}`, { signal: controller.signal }),
      apiRequest<Ruling[]>(`/cards/${id}/rulings`, { signal: controller.signal }),
    ])
      .then(([card, rulings]) => setLoaded({ state: 'done', card, rulings }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoaded({ state: 'error', error })
        }
      })
    return () => controller.abort()
  }, [id])

  if (loaded.state === 'loading') {
    return <p role="status">{t('card.loading')}</p>
  }
  if (loaded.state === 'error') {
    return <ErrorAlert error={loaded.error} />
  }

  const { card, rulings } = loaded
  // A double-faced printing has one image for each face. The first image is the image of the printing.
  const backFace = card.defaultPrinting?.imageUris.normal ? undefined : card.defaultPrinting?.faces[1]
  const otherNames = card.names.filter((name) => name.kind !== 'oracle' && !(name.kind === 'face' && name.lang === 'en'))
  return (
    <article className="page page-wide card-page">
      <div className="card-page-layout">
        <div className="card-page-image">
          <CardImage src={imageOf(card.defaultPrinting, 'large') ?? imageOf(card.defaultPrinting)} alt={card.name} />
          {backFace?.imageUris.normal && <CardImage src={backFace.imageUris.normal} alt={backFace.name} />}
        </div>
        <div className="card-page-text">
          <h1>{card.name}</h1>
          {card.faces.map((face) => (
            <FaceDetails key={face.name} face={face} showName={card.faces.length > 1} />
          ))}

          <h2>{t('card.legalities')}</h2>
          <dl className="legalities">
            {shownFormats.map((format) => (
              <div key={format} className={`legality legality-${card.legalities[format] ?? 'not_legal'}`}>
                <dt>{t(`formats.${format}`)}</dt>
                <dd>{t(`legality.${legalityKey(card.legalities[format])}`)}</dd>
              </div>
            ))}
          </dl>

          {otherNames.length > 0 && (
            <>
              <h2>{t('card.names')}</h2>
              <ul className="card-names">
                {otherNames.map((name) => (
                  <li key={`${name.lang}-${name.kind}-${name.name}`} lang={name.lang}>
                    {name.name} <span className="field-hint">({name.lang})</span>
                  </li>
                ))}
              </ul>
            </>
          )}

          {rulings.length > 0 && (
            <>
              <h2>{t('card.rulings')}</h2>
              <ul className="rulings" lang="en">
                {rulings.map((ruling) => (
                  <li key={`${ruling.publishedAt}-${ruling.comment}`}>
                    <time dateTime={ruling.publishedAt}>{ruling.publishedAt}</time> {ruling.comment}
                  </li>
                ))}
              </ul>
            </>
          )}
          <Slot name="card.page" context={{ oracleId: card.id }} />
        </div>
      </div>
      <Printings cardId={card.id} />
    </article>
  )
}

function FaceDetails({ face, showName }: { face: CardFace; showName: boolean }) {
  const stats = face.power !== null ? `${face.power}/${face.toughness}` : (face.loyalty ?? face.defense)
  return (
    <section className="card-face" lang="en">
      <p className="card-face-header">
        {showName && <strong>{face.name}</strong>} <ManaCost cost={face.manaCost} />
      </p>
      {face.typeLine && <p className="card-type">{face.typeLine}</p>}
      {face.oracleText && (
        <div className="oracle-text">
          {face.oracleText.split('\n').map((paragraph, index) => (
            <p key={index}>{paragraph}</p>
          ))}
        </div>
      )}
      {stats && <p className="card-stats">{stats}</p>}
    </section>
  )
}

const legalities = ['legal', 'not_legal', 'banned', 'restricted'] as const

function legalityKey(value: string | undefined): (typeof legalities)[number] {
  return legalities.find((legality) => legality === value) ?? 'not_legal'
}

/** The printings of a card, with a language filter, and more pages on demand. */
function Printings({ cardId }: { cardId: string }) {
  const { t } = useTranslation('cards')
  const [lang, setLang] = useState('')
  const [page, setPage] = useState<Page<Printing> | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    apiRequest<Page<Printing>>(`/cards/${cardId}/printings?limit=30${lang ? `&lang=${lang}` : ''}`, { signal: controller.signal })
      .then(setPage)
      .catch(() => undefined)
    return () => controller.abort()
  }, [cardId, lang])

  async function loadMore() {
    if (!page?.nextCursor) {
      return
    }
    const next = await apiRequest<Page<Printing>>(`/cards/${cardId}/printings?limit=30${lang ? `&lang=${lang}` : ''}&cursor=${encodeURIComponent(page.nextCursor)}`)
    setPage({ items: [...page.items, ...next.items], nextCursor: next.nextCursor })
  }

  return (
    <section aria-labelledby="printings-title">
      <h2 id="printings-title">{t('card.printings')}</h2>
      <div className="field field-inline">
        <label htmlFor="printings-lang">{t('card.language')}</label>
        <select id="printings-lang" value={lang} onChange={(event) => setLang(event.target.value)}>
          <option value="">{t('card.allLanguages')}</option>
          {['en', 'fr', 'de', 'es', 'it', 'pt', 'ja', 'ko', 'ru', 'zhs', 'zht'].map((code) => (
            <option key={code} value={code}>
              {code}
            </option>
          ))}
        </select>
      </div>
      {page && (
        <ul className="card-grid card-grid-small">
          {page.items.map((printing) => (
            <li key={printing.id}>
              <figure>
                <CardImage src={imageOf(printing, 'small')} alt={printing.printedName ?? printing.faces[0]?.name ?? printing.setName} />
                <figcaption>
                  {printing.setName} · {printing.collectorNumber} · {printing.lang}
                </figcaption>
              </figure>
            </li>
          ))}
        </ul>
      )}
      {page?.nextCursor && (
        <button type="button" onClick={() => void loadMore()}>
          {t('search.more')}
        </button>
      )}
    </section>
  )
}
