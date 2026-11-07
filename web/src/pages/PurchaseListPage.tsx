import { useEffect, useId, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { conditions, finishes, languages } from '../api/collection'
import type { Deck } from '../api/decks'
import { copyPlace, hasPreferences, knownReasons, type PurchaseCopy, type PurchaseList, type PurchaseListLine, type PurchaseReason } from '../api/purchases'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import '../components/purchases/purchases.css'
import { defaultPaperSize } from '../proxies/layout'
import { downloadFile, makeProxyPdf, proxyFileName } from '../proxies/pdf'
import { cardsToBuy, exportFileName, exportFormats, exportText, purchaseProxyItems, type CardToBuy, type ExportFormat } from '../purchases/exports'
import { Link } from '../router/Link'

type Loaded<T> = { state: 'loading' } | { state: 'error'; error: unknown } | { state: 'done'; value: T }

/**
 * The purchase list of a deck or a variant: the cards of the deck minus the available cards of the collection.
 * Only the owner of the deck can see it. See docs/architecture/purchase-list.md.
 */
export function PurchaseListPage({ id }: { id: string }) {
  const { t } = useTranslation('purchases')
  const { state, request } = useAuth()
  const [deck, setDeck] = useState<Loaded<Deck>>({ state: 'loading' })
  const [includeTokens, setIncludeTokens] = useState(false)
  const [list, setList] = useState<{ tokens: boolean; loaded: Loaded<PurchaseList> } | null>(null)

  useEffect(() => {
    if (state.status === 'loading') {
      return
    }
    request<Deck>(`/decks/${id}`)
      .then((value) => setDeck({ state: 'done', value }))
      .catch((error: unknown) => setDeck({ state: 'error', error }))
  }, [id, request, state.status])

  useEffect(() => {
    if (state.status === 'loading') {
      return
    }
    request<PurchaseList>(`/decks/${id}/purchase-list?includeTokens=${includeTokens}`)
      .then((value) => setList({ tokens: includeTokens, loaded: { state: 'done', value } }))
      .catch((error: unknown) => setList({ tokens: includeTokens, loaded: { state: 'error', error } }))
  }, [id, includeTokens, request, state.status])

  if (deck.state === 'loading') {
    return <p role="status">{t('page.loading')}</p>
  }
  if (deck.state === 'error') {
    return <ErrorAlert error={deck.error} />
  }

  const current = list && list.tokens === includeTokens ? list.loaded : { state: 'loading' as const }
  return (
    <article className="page page-wide purchase-page">
      <header className="page-header">
        <h1>{t('page.title', { name: deck.value.name })}</h1>
        <Link to={`/decks/${id}`}>{t('page.openDeck')}</Link>
      </header>
      <p className="field-hint">{t('page.help')}</p>
      <label className="checkbox">
        <input type="checkbox" checked={includeTokens} onChange={(event) => setIncludeTokens(event.target.checked)} />
        {t('page.includeTokens')}
      </label>
      {current.state === 'loading' && <p role="status">{t('page.loading')}</p>}
      {current.state === 'error' && <ErrorAlert error={current.error} />}
      {current.state === 'done' && <PurchaseTable list={current.value} deckName={deck.value.name} />}
    </article>
  )
}

function PurchaseTable({ list, deckName }: { list: PurchaseList; deckName: string }) {
  const { t } = useTranslation(['purchases', 'decks'])
  const cards = useMemo(() => cardsToBuy(list), [list])
  const locked = list.lines.some((line) => line.fromCollection.some((copy) => copy.locked))

  if (list.lines.length === 0) {
    return <p>{t('page.empty')}</p>
  }

  return (
    <>
      <p className="purchase-total" role="status">
        {list.totalToBuy === 0 ? t('page.nothingToBuy') : t('page.total', { count: list.totalToBuy })}
      </p>
      {locked && <p className="alert alert-warning">{t('page.lockedAlert')}</p>}
      <div className="table-scroll">
        <table className="table">
          <caption className="visually-hidden">{t('page.caption')}</caption>
          <thead>
            <tr>
              <th scope="col">{t('page.columns.card')}</th>
              <th scope="col">{t('page.columns.zone')}</th>
              <th scope="col">{t('page.columns.needed')}</th>
              <th scope="col">{t('page.columns.inDeck')}</th>
              <th scope="col">{t('page.columns.fromCollection')}</th>
              <th scope="col">{t('page.columns.toBuy')}</th>
            </tr>
          </thead>
          <tbody>
            {list.lines.map((line) => (
              <PurchaseRow key={`${line.oracleId}-${line.zone}`} list={list} line={line} />
            ))}
          </tbody>
        </table>
      </div>
      {cards.length > 0 && (
        <>
          <ExportPanel cards={cards} deckName={deckName} />
          <ProxyButton list={list} cards={cards} deckName={deckName} />
        </>
      )}
    </>
  )
}

function PurchaseRow({ list, line }: { list: PurchaseList; line: PurchaseListLine }) {
  const { t } = useTranslation(['purchases', 'decks'])
  const name = list.cards[line.oracleId]?.name ?? line.oracleId
  return (
    <tr>
      <th scope="row">
        <Link to={`/cards/${line.oracleId}`}>{name}</Link>
        {hasPreferences(line.purchasePreferences) && <span className="badge">{t('preferences.marker')}</span>}
      </th>
      <td>{t(`decks:zones.${line.zone}`)}</td>
      <td>{line.quantity}</td>
      <td>{line.inDeck}</td>
      <td>
        {line.fromCollection.length > 0 && (
          <ul className="purchase-copies">
            {line.fromCollection.map((copy, index) => (
              <li key={`${copy.locationId}-${index}`}>
                {t('page.fromCopy', { count: copy.quantity, place: copyPlace(copy) })}
                {copy.locked && (
                  <>
                    {' '}
                    <span className="badge">
                      <span aria-hidden="true">🔒 </span>
                      {t('page.locked')}
                    </span>{' '}
                    <span className="purchase-closest">{t('page.lockedHint', { place: copyPlace(copy) })}</span>
                  </>
                )}
              </li>
            ))}
          </ul>
        )}
      </td>
      <td>
        <span className={line.toBuy > 0 ? 'purchase-to-buy' : undefined}>{line.toBuy}</span>
        {line.toBuy > 0 && line.closest.length > 0 && (
          <div className="purchase-closest">
            <span className="visually-hidden">{t('page.closestTitle')}</span>
            <ul className="purchase-copies">
              {line.closest.map((copy, index) => (
                <li key={`${copy.locationId}-${index}`}>
                  <ClosestCopy list={list} line={line} copy={copy} />
                </li>
              ))}
            </ul>
          </div>
        )}
      </td>
    </tr>
  )
}

/** A closest copy as a sentence, for example "Not in French: 1 English copy in Rack 2". */
function ClosestCopy({ list, line, copy }: { list: PurchaseList; line: PurchaseListLine; copy: PurchaseCopy }) {
  const { t, i18n } = useTranslation(['purchases', 'collection'])
  const preferences = line.purchasePreferences

  function languageName(code: string): string {
    const known = languages.find((value) => value === code)
    return known ? t(`languages.${known}`) : code
  }

  function reasonText(reason: PurchaseReason): string {
    switch (reason) {
      case 'printing':
        return (preferences?.wantedPrintingIds?.length ?? 0) > 0 ? t('page.reasons.printingWanted') : t('page.reasons.printingAvoided')
      case 'language': {
        const names = (preferences?.languages ?? []).map(languageName)
        return t('page.reasons.language', { languages: new Intl.ListFormat(i18n.resolvedLanguage ?? 'en', { type: 'disjunction' }).format(names) })
      }
      case 'finish':
        return preferences?.foil === 'foil' ? t('page.reasons.finishFoil') : preferences?.foil === 'nonfoil' ? t('page.reasons.finishNonfoil') : t('page.reasons.finish')
      case 'condition': {
        const minimum = conditions.find((value) => value === preferences?.minimumCondition)
        return minimum ? t('page.reasons.condition', { condition: t(`collection:conditions.${minimum}`) }) : t('page.reasons.conditionUnknown')
      }
      case 'unavailable':
        return t('page.reasons.unavailable')
    }
  }

  const printing = copy.printingId ? list.printings[copy.printingId] : undefined
  const finish = finishes.find((value) => value === copy.finish)
  const condition = conditions.find((value) => value === copy.condition)
  const details = [
    printing ? `${printing.setCode.toUpperCase()} ${printing.collectorNumber}` : null,
    finish ? t(`collection:finishes.${finish}`) : null,
    condition ? t(`collection:conditions.${condition}`) : null,
  ].filter(Boolean)
  const copies = t('page.copies', { count: copy.quantity, language: languageName(copy.language) })
  return (
    <>
      {t('page.closest', {
        reasons: knownReasons(copy).map(reasonText).join(' · '),
        copies: details.length > 0 ? `${copies} (${details.join(', ')})` : copies,
        place: copyPlace(copy),
      })}
      {copy.locked && <span className="badge">{t('page.locked')}</span>}
    </>
  )
}

/** The export of the cards to buy as text: a copy to the clipboard or a download. */
function ExportPanel({ cards, deckName }: { cards: CardToBuy[]; deckName: string }) {
  const { t } = useTranslation('purchases')
  const id = useId()
  const [format, setFormat] = useState<ExportFormat>('cardmarket')
  const [status, setStatus] = useState<'idle' | 'copied' | 'failed'>('idle')
  const text = exportText(format, cards)

  async function copy() {
    try {
      await navigator.clipboard.writeText(text)
      setStatus('copied')
    } catch {
      setStatus('failed')
    }
  }

  return (
    <section className="purchase-export" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{t('export.title')}</h2>
      <div className="field">
        <label htmlFor={`${id}-format`}>{t('export.format')}</label>
        <select
          id={`${id}-format`}
          value={format}
          aria-describedby={`${id}-hint`}
          onChange={(event) => {
            setFormat(event.target.value as ExportFormat)
            setStatus('idle')
          }}
        >
          {exportFormats.map((value) => (
            <option key={value} value={value}>
              {t(`export.formats.${value}`)}
            </option>
          ))}
        </select>
        <p className="field-hint" id={`${id}-hint`}>
          {t(`export.hints.${format}`)}
        </p>
      </div>
      <div className="field">
        <label htmlFor={`${id}-text`}>{t('export.preview')}</label>
        <textarea id={`${id}-text`} readOnly rows={Math.min(12, cards.length + 1)} spellCheck={false} value={text} />
      </div>
      <div className="form-actions">
        <button type="button" className="button-primary" onClick={() => void copy()}>
          {t('export.copy')}
        </button>
        <button type="button" onClick={() => downloadFile(new Blob([text], { type: 'text/plain;charset=utf-8' }), exportFileName(deckName, format))}>
          {t('export.download')}
        </button>
      </div>
      <p role="status" className={status === 'failed' ? 'field-error' : 'field-hint'}>
        {status === 'copied' ? t('export.copied') : status === 'failed' ? t('export.copyFailed') : ''}
      </p>
    </section>
  )
}

type ProxyStatus = { state: 'idle' } | { state: 'working' } | { state: 'done'; failed: number } | { state: 'error' }

/** Prints proxies of the cards to buy with the PDF function of the deck page. */
function ProxyButton({ list, cards, deckName }: { list: PurchaseList; cards: CardToBuy[]; deckName: string }) {
  const { t } = useTranslation('purchases')
  const [status, setStatus] = useState<ProxyStatus>({ state: 'idle' })

  async function print() {
    setStatus({ state: 'working' })
    try {
      const pdf = await makeProxyPdf(purchaseProxyItems(list, cards), { paper: defaultPaperSize(typeof navigator === 'undefined' ? undefined : navigator.language) })
      downloadFile(pdf.blob, proxyFileName(deckName))
      setStatus({ state: 'done', failed: pdf.failed.length })
    } catch (caught) {
      console.error('The proxy PDF failed.', caught)
      setStatus({ state: 'error' })
    }
  }

  return (
    <div className="form-actions">
      <button type="button" disabled={status.state === 'working'} onClick={() => void print()}>
        {t('proxies.print')}
      </button>
      <p role="status" className="field-hint">
        {status.state === 'working' && t('proxies.working')}
        {status.state === 'done' && (status.failed > 0 ? t('proxies.failed', { count: status.failed }) : t('proxies.done'))}
        {status.state === 'error' && t('proxies.error')}
      </p>
    </div>
  )
}
