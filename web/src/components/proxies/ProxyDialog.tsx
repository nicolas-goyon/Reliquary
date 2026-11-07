import { useEffect, useId, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import type { Printing } from '../../api/cards'
import type { Deck } from '../../api/decks'
import { useAuth } from '../../auth/session'
import { defaultPaperSize, pageCount, paperSizes, type PaperSize } from '../../proxies/layout'
import { downloadFile, makeProxyPdf, proxyFileName } from '../../proxies/pdf'
import { cardTotal, hasFaces, linesOfZones, optionalZones, orderedLines, proxyItems, proxyItemsWithBackFaces, selectedLines, type OptionalZone } from '../../proxies/selection'
import './proxies.css'

const focusableSelector = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

type Status =
  | { state: 'idle' }
  | { state: 'loading'; loaded: number; total: number }
  | { state: 'done'; failed: string[] }
  | { state: 'error' }

/**
 * The dialog that prints proxies of a deck as a PDF file.
 * The user selects all the cards of the deck, with optional zones, or selects lines one by one.
 */
export function ProxyDialog({ deck, onClose }: { deck: Deck; onClose(): void }) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const id = useId()
  const dialog = useRef<HTMLDivElement>(null)
  const open = useRef(true)
  const [source, setSource] = useState<'deck' | 'selection'>('deck')
  const [addedZones, setAddedZones] = useState<OptionalZone[]>([])
  const [lineIds, setLineIds] = useState<ReadonlySet<string>>(new Set())
  const [paper, setPaper] = useState<PaperSize>(() => defaultPaperSize(typeof navigator === 'undefined' ? undefined : navigator.language))
  const [backFaces, setBackFaces] = useState(false)
  const [status, setStatus] = useState<Status>({ state: 'idle' })

  const allLines = useMemo(() => orderedLines(deck).filter((line) => line.quantity > 0), [deck])
  const lines = source === 'deck' ? linesOfZones(deck, addedZones) : selectedLines(deck, lineIds)
  const total = cardTotal(proxyItems(deck, lines))
  const doubleFaced = allLines.some((line) => hasFaces(deck, line))
  const working = status.state === 'loading'

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    dialog.current?.querySelector<HTMLElement>(focusableSelector)?.focus()
    return () => {
      open.current = false
      previous?.focus()
    }
  }, [])

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      onClose()
      return
    }
    if (event.key !== 'Tab' || !dialog.current) {
      return
    }
    const focusable = [...dialog.current.querySelectorAll<HTMLElement>(focusableSelector)]
    const first = focusable[0]
    const last = focusable[focusable.length - 1]
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  function toggleZone(zone: OptionalZone, checked: boolean) {
    setAddedZones((current) => (checked ? [...current, zone] : current.filter((value) => value !== zone)))
  }

  function toggleLine(lineId: string, checked: boolean) {
    setLineIds((current) => {
      const next = new Set(current)
      if (checked) {
        next.add(lineId)
      } else {
        next.delete(lineId)
      }
      return next
    })
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (total === 0 || working) {
      return
    }
    setStatus({ state: 'loading', loaded: 0, total: 0 })
    try {
      const items = backFaces ? await proxyItemsWithBackFaces(deck, lines, (printingId) => request<Printing>(`/printings/${printingId}`)) : proxyItems(deck, lines)
      const pdf = await makeProxyPdf(items, {
        paper,
        onProgress: (loaded, count) => {
          if (open.current) {
            setStatus({ state: 'loading', loaded, total: count })
          }
        },
      })
      if (!open.current) {
        return
      }
      downloadFile(pdf.blob, proxyFileName(deck.name))
      setStatus({ state: 'done', failed: pdf.failed })
    } catch (caught) {
      console.error('The proxy PDF failed.', caught)
      if (open.current) {
        setStatus({ state: 'error' })
      }
    }
  }

  return createPortal(
    <div className="dialog-backdrop" onClick={onClose}>
      <div
        ref={dialog}
        className="dialog-modal panel proxy-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${id}-title`}
        aria-describedby={`${id}-description`}
        onKeyDown={handleKeyDown}
        onClick={(event) => event.stopPropagation()}
      >
        <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
          <h2 id={`${id}-title`}>{t('proxies.title')}</h2>
          <p id={`${id}-description`} className="field-hint">
            {t('proxies.description')}
          </p>

          <fieldset className="proxy-group" disabled={working}>
            <legend>{t('proxies.source')}</legend>
            <label className="checkbox">
              <input type="radio" name={`${id}-source`} checked={source === 'deck'} onChange={() => setSource('deck')} />
              {t('proxies.sourceDeck')}
            </label>
            {source === 'deck' && (
              <div className="proxy-options">
                {optionalZones
                  .filter((zone) => deck.counts[zone] > 0)
                  .map((zone) => (
                    <label key={zone} className="checkbox">
                      <input type="checkbox" checked={addedZones.includes(zone)} onChange={(event) => toggleZone(zone, event.target.checked)} />
                      {t('proxies.addZone', { zone: t(`zones.${zone}`) })}
                    </label>
                  ))}
              </div>
            )}
            <label className="checkbox">
              <input type="radio" name={`${id}-source`} checked={source === 'selection'} onChange={() => setSource('selection')} />
              {t('proxies.sourceSelection')}
            </label>
          </fieldset>

          {source === 'selection' && (
            <fieldset className="proxy-group" disabled={working}>
              <legend>{t('proxies.lines')}</legend>
              <div className="form-actions">
                <button type="button" className="button-small" onClick={() => setLineIds(new Set(allLines.map((line) => line.id)))}>
                  {t('proxies.selectAll')}
                </button>
                <button type="button" className="button-small" onClick={() => setLineIds(new Set())}>
                  {t('proxies.selectNone')}
                </button>
              </div>
              <ul className="proxy-lines">
                {allLines.map((line) => (
                  <li key={line.id}>
                    <label className="checkbox">
                      <input type="checkbox" checked={lineIds.has(line.id)} onChange={(event) => toggleLine(line.id, event.target.checked)} />
                      {t('proxies.line', { quantity: line.quantity, name: deck.cards[line.oracleId]?.name ?? line.oracleId })}
                      <span className="proxy-zone">{t(`zones.${line.zone}`)}</span>
                    </label>
                  </li>
                ))}
              </ul>
            </fieldset>
          )}

          <div className="field">
            <label htmlFor={`${id}-paper`}>{t('proxies.paper')}</label>
            <select id={`${id}-paper`} value={paper} disabled={working} onChange={(event) => setPaper(event.target.value as PaperSize)}>
              {paperSizes.map((size) => (
                <option key={size} value={size}>
                  {t(`proxies.papers.${size}`)}
                </option>
              ))}
            </select>
            <p className="field-hint">{t('proxies.scaleHint')}</p>
          </div>

          {doubleFaced && (
            <label className="checkbox">
              <input type="checkbox" checked={backFaces} disabled={working} onChange={(event) => setBackFaces(event.target.checked)} />
              {t('proxies.backFaces')}
            </label>
          )}

          <p className="proxy-total" aria-live="polite">
            {total === 0 ? t('proxies.noCards') : `${t('proxies.cardCount', { count: total })} · ${t('proxies.pageCount', { count: pageCount(total) })}`}
          </p>

          {status.state === 'loading' && (
            <div className="proxy-progress" role="status">
              <progress max={Math.max(status.total, 1)} value={status.loaded} aria-label={t('proxies.progressLabel')} />
              <span>{t('proxies.progress', { loaded: status.loaded, total: status.total })}</span>
            </div>
          )}
          {status.state === 'done' && status.failed.length === 0 && (
            <p className="alert alert-success" role="status">
              {t('proxies.done')}
            </p>
          )}
          {status.state === 'done' && status.failed.length > 0 && (
            <div className="alert alert-error" role="alert">
              <p>{t('proxies.failed', { count: status.failed.length })}</p>
              <ul>
                {status.failed.map((name) => (
                  <li key={name}>{name}</li>
                ))}
              </ul>
            </div>
          )}
          {status.state === 'error' && (
            <p className="alert alert-error" role="alert">
              {t('proxies.error')}
            </p>
          )}

          <div className="form-actions">
            <button type="submit" className="button-primary" disabled={total === 0 || working}>
              {t('proxies.submit')}
            </button>
            <button type="button" onClick={onClose}>
              {status.state === 'done' ? t('proxies.close') : t('actions.cancel')}
            </button>
          </div>
        </form>
      </div>
    </div>,
    document.body,
  )
}
