import { useEffect, useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../api/client'
import type { Deck } from '../../api/decks'
import { compareUrl, maxMessageLength, maxVariantNameLength, type DeckHistory, type DeckVariant, type DeckVersion } from '../../api/versions'
import { useAuth } from '../../auth/session'
import { Link } from '../../router/Link'
import { navigate } from '../../router/navigation'
import { ErrorAlert, TextField } from '../Forms'
import { Dialog } from './Dialog'
import './versions.css'

/**
 * The variants of a deck: the main deck and its variants, with a link to each list.
 * On a variant, the bar tells the main deck. The owner creates a variant from the main deck, and merges a variant into the main deck.
 */
export function VariantBar({ deck }: { deck: Deck }) {
  const { t } = useTranslation('versions')
  const { request } = useAuth()
  const [variants, setVariants] = useState<DeckVariant[] | null>(null)
  const [mainName, setMainName] = useState<string | null>(null)
  const [dialog, setDialog] = useState<'create' | 'merge' | null>(null)
  const [merged, setMerged] = useState<DeckVersion | null>(null)
  const mainId = deck.variantOf ?? deck.id
  const isVariant = Boolean(deck.variantOf)

  useEffect(() => {
    request<DeckVariant[]>(`/decks/${deck.id}/variants`)
      .then(setVariants)
      .catch(() => setVariants([]))
  }, [deck.id, request])

  useEffect(() => {
    if (!deck.variantOf) {
      return
    }
    request<Deck>(`/decks/${deck.variantOf}`)
      .then((main) => setMainName(main.name))
      .catch(() => setMainName(null))
  }, [deck.variantOf, request])

  if (variants === null || (!isVariant && variants.length === 0 && !deck.isOwner)) {
    return null
  }

  const mainLabel = isVariant ? (mainName ?? t('variants.mainDeck')) : deck.name
  return (
    <section className="variant-bar" aria-labelledby="variant-bar-title">
      <h2 id="variant-bar-title" className="visually-hidden">
        {t('variants.title')}
      </h2>
      {isVariant && (
        <p className="alert alert-info variant-notice">
          {t('variants.variantOf')} <Link to={`/decks/${mainId}`}>{mainLabel}</Link>
        </p>
      )}
      <div className="variant-bar-row">
        <nav aria-label={t('variants.listLabel')}>
          <ul className="variant-list">
            <li>
              <Link to={`/decks/${mainId}`} aria-current={isVariant ? undefined : 'page'}>
                {t('variants.mainDeckName', { name: mainLabel })}
              </Link>
            </li>
            {variants.map((variant) => (
              <li key={variant.id}>
                <Link to={`/decks/${variant.id}`} aria-current={variant.id === deck.id ? 'page' : undefined}>
                  {variant.name}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
        {!isVariant && variants.length === 0 && <span className="field-hint">{t('variants.none')}</span>}
        <div className="variant-actions">
          {isVariant && (
            <Link to={compareUrl({ deckId: mainId, versionId: null }, { deckId: deck.id, versionId: null })}>{t('variants.compareMain')}</Link>
          )}
          {deck.isOwner && !isVariant && (
            <button type="button" onClick={() => setDialog('create')}>
              {t('variants.create')}
            </button>
          )}
          {deck.isOwner && isVariant && (
            <button type="button" onClick={() => setDialog('merge')}>
              {t('variants.merge')}
            </button>
          )}
        </div>
      </div>
      {merged && (
        <p className="alert alert-success" role="status">
          {t('merge.done', { number: merged.number })} <Link to={`/decks/${mainId}`}>{t('merge.openMain')}</Link>
        </p>
      )}
      {dialog === 'create' && <CreateVariantDialog deck={deck} onClose={() => setDialog(null)} />}
      {dialog === 'merge' && (
        <MergeDialog
          deck={deck}
          mainName={mainLabel}
          onClose={() => setDialog(null)}
          onMerged={(version) => {
            setDialog(null)
            setMerged(version)
          }}
        />
      )}
    </section>
  )
}

/** The dialog of a new variant: its name, and the version of the main deck where it starts. */
function CreateVariantDialog({ deck, onClose }: { deck: Deck; onClose(): void }) {
  const { t } = useTranslation('versions')
  const { request } = useAuth()
  const id = useId()
  const [name, setName] = useState('')
  const [baseVersionId, setBaseVersionId] = useState('')
  const [versions, setVersions] = useState<DeckVersion[]>([])
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    request<DeckHistory>(`/decks/${deck.id}/versions`)
      .then((history) => setVersions(history.versions))
      .catch(() => setVersions([]))
  }, [deck.id, request])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const variant = await request<DeckVariant>(`/decks/${deck.id}/variants`, { method: 'POST', body: { name: name.trim(), baseVersionId: baseVersionId || null } })
      onClose()
      navigate(`/decks/${variant.id}`)
    } catch (caught) {
      setError(caught)
      setBusy(false)
    }
  }

  return (
    <Dialog title={t('createVariant.title')} onClose={onClose}>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <TextField
          label={t('createVariant.name')}
          field="name"
          error={error}
          data-autofocus
          required
          maxLength={maxVariantNameLength}
          value={name}
          hint={t('createVariant.nameHint')}
          onChange={(event) => setName(event.target.value)}
        />
        <div className="field">
          <label htmlFor={`${id}-base`}>{t('createVariant.base')}</label>
          <select id={`${id}-base`} value={baseVersionId} onChange={(event) => setBaseVersionId(event.target.value)}>
            <option value="">{versions.length > 0 ? t('createVariant.latest', { number: versions[0].number }) : t('createVariant.latestNone')}</option>
            {versions.map((version) => (
              <option key={version.id} value={version.id}>
                {t('createVariant.version', { number: version.number, message: version.message })}
              </option>
            ))}
          </select>
        </div>
        {!(error instanceof ApiError && error.fieldCode('name')) && <ErrorAlert error={error} />}
        <div className="form-actions">
          <button type="submit" className="button-primary" disabled={busy || name.trim() === ''}>
            {t('createVariant.submit')}
          </button>
          <button type="button" onClick={onClose}>
            {t('actions.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

interface MergeDialogProps {
  deck: Deck
  mainName: string
  onClose(): void
  onMerged(version: DeckVersion): void
}

/** The dialog of a merge of a variant into its main deck, with an optional message. */
function MergeDialog({ deck, mainName, onClose, onMerged }: MergeDialogProps) {
  const { t } = useTranslation('versions')
  const { request } = useAuth()
  const [text, setText] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      onMerged(await request<DeckVersion>(`/decks/${deck.id}/merge`, { method: 'POST', body: { message: text.trim() || null } }))
    } catch (caught) {
      setError(caught)
      setBusy(false)
    }
  }

  return (
    <Dialog title={t('merge.title')} onClose={onClose}>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <p>{t('merge.help', { variant: deck.name, main: mainName })}</p>
        <TextField
          label={t('merge.message')}
          field="message"
          error={error}
          data-autofocus
          maxLength={maxMessageLength}
          value={text}
          hint={t('merge.messageHint')}
          onChange={(event) => setText(event.target.value)}
        />
        {!(error instanceof ApiError && error.fieldCode('message')) && <ErrorAlert error={error} />}
        <div className="form-actions">
          <button type="submit" className="button-primary" disabled={busy}>
            {t('merge.submit')}
          </button>
          <button type="button" onClick={onClose}>
            {t('actions.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
