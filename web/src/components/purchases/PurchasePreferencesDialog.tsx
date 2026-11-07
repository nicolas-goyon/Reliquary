import { useEffect, useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Page, PrintingSummary } from '../../api/cards'
import { apiRequest } from '../../api/client'
import { conditions, languages, type Condition } from '../../api/collection'
import type { Deck, DeckLine } from '../../api/decks'
import { currencies, foilPreferences, type FoilPreference, type PurchasePreferences } from '../../api/purchases'
import { useAuth } from '../../auth/session'
import { ErrorAlert } from '../Forms'
import { Dialog } from '../versions/Dialog'
import './purchases.css'

type Choice = 'accepted' | 'wanted' | 'avoided'

type PrintingState = { state: 'loading' } | { state: 'error' } | { state: 'done'; items: PrintingSummary[]; nextCursor: string | null }

interface DialogProps {
  deck: Deck
  line: DeckLine
  onSaved(deck: Deck): void
  onClose(): void
}

function initialChoices(preferences: PurchasePreferences | null | undefined): Record<string, Choice> {
  const choices: Record<string, Choice> = {}
  for (const id of preferences?.wantedPrintingIds ?? []) {
    choices[id] = 'wanted'
  }
  for (const id of preferences?.avoidedPrintingIds ?? []) {
    choices[id] = 'avoided'
  }
  return choices
}

/** Reads an amount that the user typed, with a point or a comma. The result is NaN for a text that is not a number. */
function parseAmount(text: string): number {
  const clean = text.trim().replace(',', '.')
  return /^\d+(\.\d+)?$/.test(clean) ? Number(clean) : Number.NaN
}

/**
 * The dialog of the purchase preferences of a deck line: languages, foil, minimum condition, wanted or avoided printings and maximum price.
 * The new preferences replace all the preferences of the line. See docs/architecture/purchase-list.md.
 */
export function PurchasePreferencesDialog({ deck, line, onSaved, onClose }: DialogProps) {
  const { t } = useTranslation(['purchases', 'collection'])
  const { request } = useAuth()
  const id = useId()
  const preferences = line.purchasePreferences
  const name = deck.cards[line.oracleId]?.name ?? line.oracleId
  const [selectedLanguages, setSelectedLanguages] = useState<string[]>(preferences?.languages ?? [])
  const [foil, setFoil] = useState<FoilPreference>(preferences?.foil ?? 'any')
  const [condition, setCondition] = useState<Condition | ''>((conditions.find((value) => value === preferences?.minimumCondition) ?? '') as Condition | '')
  const [choices, setChoices] = useState<Record<string, Choice>>(() => initialChoices(preferences))
  const [amount, setAmount] = useState(preferences?.maximumPrice ? String(preferences.maximumPrice.amount) : '')
  const [currency, setCurrency] = useState(preferences?.maximumPrice?.currency ?? 'EUR')
  const [printings, setPrintings] = useState<PrintingState>({ state: 'loading' })
  const [priceError, setPriceError] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    apiRequest<Page<PrintingSummary>>(`/cards/${line.oracleId}/printings?limit=30`, { signal: controller.signal })
      .then((page) => setPrintings({ state: 'done', items: page.items, nextCursor: page.nextCursor }))
      .catch(() => {
        if (!controller.signal.aborted) {
          setPrintings({ state: 'error' })
        }
      })
    return () => controller.abort()
  }, [line.oracleId])

  async function loadMore() {
    if (printings.state !== 'done' || !printings.nextCursor) {
      return
    }
    try {
      const page = await apiRequest<Page<PrintingSummary>>(`/cards/${line.oracleId}/printings?limit=30&cursor=${encodeURIComponent(printings.nextCursor)}`)
      setPrintings({ state: 'done', items: [...printings.items, ...page.items], nextCursor: page.nextCursor })
    } catch {
      setPrintings({ state: 'error' })
    }
  }

  function toggleLanguage(language: string, checked: boolean) {
    setSelectedLanguages((current) => (checked ? [...current, language] : current.filter((value) => value !== language)))
  }

  function setChoice(printingId: string, choice: Choice) {
    setChoices((current) => ({ ...current, [printingId]: choice }))
  }

  async function send(body: unknown) {
    setError(null)
    setBusy(true)
    try {
      onSaved(await request<Deck>(`/decks/${deck.id}/lines/${line.id}`, { method: 'PATCH', body }))
      onClose()
    } catch (caught) {
      setError(caught)
    } finally {
      setBusy(false)
    }
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const price = amount.trim() ? parseAmount(amount) : null
    if (price !== null && !(price > 0)) {
      setPriceError(true)
      return
    }
    setPriceError(false)
    const wanted = Object.keys(choices).filter((key) => choices[key] === 'wanted')
    const avoided = Object.keys(choices).filter((key) => choices[key] === 'avoided')
    const next: PurchasePreferences = {
      wantedPrintingIds: wanted,
      avoidedPrintingIds: avoided,
      languages: selectedLanguages,
      minimumCondition: condition || null,
      foil,
      maximumPrice: price === null ? null : { amount: price, currency },
    }
    const empty = wanted.length === 0 && avoided.length === 0 && selectedLanguages.length === 0 && !condition && foil === 'any' && price === null
    void send(empty ? { clearPurchasePreferences: true } : { purchasePreferences: next })
  }

  const loaded = printings.state === 'done' ? printings.items : []
  // A chosen printing that is not in the loaded pages stays visible, so that the user can change it.
  const otherIds = Object.keys(choices).filter((key) => choices[key] !== 'accepted' && !loaded.some((printing) => printing.id === key))

  function printingRow(printingId: string, label: string) {
    return (
      <li key={printingId} className="purchase-printing">
        <label htmlFor={`${id}-printing-${printingId}`} className="visually-hidden">
          {t('preferences.printingChoice', { printing: label })}
        </label>
        <span aria-hidden="true">{label}</span>
        <select id={`${id}-printing-${printingId}`} value={choices[printingId] ?? 'accepted'} onChange={(event) => setChoice(printingId, event.target.value as Choice)}>
          {(['accepted', 'wanted', 'avoided'] as const).map((choice) => (
            <option key={choice} value={choice}>
              {t(`preferences.choices.${choice}`)}
            </option>
          ))}
        </select>
      </li>
    )
  }

  return (
    <Dialog title={t('preferences.title', { name })} onClose={onClose} wide>
      <form className="form purchase-preferences" onSubmit={handleSubmit} noValidate aria-busy={busy}>
        <p className="field-hint">{t('preferences.help')}</p>

        <fieldset className="purchase-group">
          <legend>{t('preferences.languages')}</legend>
          <p className="field-hint">{t('preferences.languagesHint')}</p>
          <div className="purchase-options">
            {languages.map((language) => (
              <label key={language} className="checkbox">
                <input type="checkbox" checked={selectedLanguages.includes(language)} onChange={(event) => toggleLanguage(language, event.target.checked)} />
                {t(`collection:languages.${language}`)}
              </label>
            ))}
          </div>
        </fieldset>

        <div className="purchase-fields">
          <div className="field">
            <label htmlFor={`${id}-foil`}>{t('preferences.foil')}</label>
            <select id={`${id}-foil`} value={foil} onChange={(event) => setFoil(event.target.value as FoilPreference)}>
              {foilPreferences.map((value) => (
                <option key={value} value={value}>
                  {t(`preferences.foilValues.${value}`)}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor={`${id}-condition`}>{t('preferences.minimumCondition')}</label>
            <select id={`${id}-condition`} value={condition} onChange={(event) => setCondition(event.target.value as Condition | '')}>
              <option value="">{t('preferences.anyCondition')}</option>
              {conditions.map((value) => (
                <option key={value} value={value}>
                  {t(`collection:conditions.${value}`)}
                </option>
              ))}
            </select>
          </div>
        </div>

        <fieldset className="purchase-group">
          <legend>{t('preferences.printings')}</legend>
          <p className="field-hint">{t('preferences.printingsHint')}</p>
          {printings.state === 'loading' && <p role="status">{t('preferences.loadingPrintings')}</p>}
          {printings.state === 'error' && <p className="alert alert-warning">{t('preferences.printingsError')}</p>}
          <ul className="purchase-printings">
            {loaded.map((printing) => printingRow(printing.id, `${printing.setName} (${printing.setCode.toUpperCase()} ${printing.collectorNumber}, ${printing.lang})`))}
            {otherIds.map((printingId) => {
              const known = deck.printings[printingId]
              return printingRow(printingId, known ? `${known.setCode.toUpperCase()} ${known.collectorNumber} (${known.lang})` : `${t('preferences.otherPrinting')} ${printingId.slice(0, 8)}`)
            })}
          </ul>
          {printings.state === 'done' && printings.nextCursor && (
            <button type="button" className="button-small" onClick={() => void loadMore()}>
              {t('preferences.morePrintings')}
            </button>
          )}
        </fieldset>

        <fieldset className="purchase-group">
          <legend>{t('preferences.maximumPrice')}</legend>
          <div className="purchase-fields">
            <div className="field">
              <label htmlFor={`${id}-amount`}>{t('preferences.amount')}</label>
              <input
                id={`${id}-amount`}
                inputMode="decimal"
                value={amount}
                aria-invalid={priceError ? true : undefined}
                aria-describedby={priceError ? `${id}-price-hint ${id}-price-error` : `${id}-price-hint`}
                onChange={(event) => setAmount(event.target.value)}
              />
            </div>
            <div className="field">
              <label htmlFor={`${id}-currency`}>{t('preferences.currency')}</label>
              <select id={`${id}-currency`} value={currency} onChange={(event) => setCurrency(event.target.value)}>
                {[...new Set<string>([...currencies, currency])].map((code) => (
                  <option key={code} value={code}>
                    {code}
                  </option>
                ))}
              </select>
            </div>
          </div>
          <p className="field-hint" id={`${id}-price-hint`}>
            {t('preferences.priceHint')}
          </p>
          {priceError && (
            <p className="field-error" id={`${id}-price-error`}>
              {t('preferences.invalidPrice')}
            </p>
          )}
        </fieldset>

        <ErrorAlert error={error} />
        <div className="form-actions">
          <button type="submit" className="button-primary" disabled={busy}>
            {t('preferences.save')}
          </button>
          {preferences && (
            <button type="button" disabled={busy} onClick={() => void send({ clearPurchasePreferences: true })}>
              {t('preferences.clear')}
            </button>
          )}
          <button type="button" onClick={onClose}>
            {t('preferences.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
