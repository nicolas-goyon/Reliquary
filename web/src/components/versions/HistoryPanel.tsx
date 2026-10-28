import { useCallback, useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../api/client'
import { zones, type Deck } from '../../api/decks'
import {
  compareUrl,
  formatVersionDate,
  maxMessageLength,
  noChangesCode,
  versionKindKey,
  zoneOf,
  type DeckHistory,
  type DeckVersion,
  type DeckVersionDetail,
  type ListChanges,
} from '../../api/versions'
import { flattenLocations, locationLabel, type CollectionLocation } from '../../api/collection'
import { useAuth } from '../../auth/session'
import { Link } from '../../router/Link'
import { ErrorAlert } from '../Forms'
import { Dialog } from './Dialog'
import { ListChangesView } from './ListChangesView'
import './versions.css'

interface HistoryPanelProps {
  deck: Deck
  /** Receives the deck again after an operation that changes the working list. */
  onChange(deck: Deck): void
}

type Loaded = { state: 'loading' } | { state: 'error'; error: unknown } | { state: 'done'; history: DeckHistory }
type Selection = { version: DeckVersion; view: 'lines' | 'changes' } | null
type Pending = { action: 'commit' } | { action: 'release' } | { action: 'restore' | 'revert' | 'assemble'; version: DeckVersion } | null

/**
 * The history of a deck: the versions, the state of the working list, and the version operations.
 * Only the owner can commit, restore, revert, mark the assembled version and release the assembled deck.
 */
export function HistoryPanel({ deck, onChange }: HistoryPanelProps) {
  const { t, i18n } = useTranslation('versions')
  const { request } = useAuth()
  const [loaded, setLoaded] = useState<Loaded>({ state: 'loading' })
  const [reload, setReload] = useState(0)
  const [selection, setSelection] = useState<Selection>(null)
  const [pending, setPending] = useState<Pending>(null)
  const [message, setMessage] = useState<string | null>(null)
  const language = i18n.resolvedLanguage ?? 'en'
  const owner = deck.isOwner

  useEffect(() => {
    request<DeckHistory>(`/decks/${deck.id}/versions`)
      .then((history) => setLoaded({ state: 'done', history }))
      .catch((error: unknown) => setLoaded({ state: 'error', error }))
  }, [deck.id, deck.version, reload, request])

  /** Shows the new version, and gets the deck and the history again. */
  const created = useCallback(
    async (version: DeckVersion) => {
      setPending(null)
      setSelection(null)
      setMessage(t('history.created', { number: version.number }))
      setReload((value) => value + 1)
      // The version exists now. A failure of this reload must not show the operation as failed.
      await request<Deck>(`/decks/${deck.id}`)
        .then(onChange)
        .catch(() => undefined)
    },
    [deck.id, onChange, request, t],
  )

  /** Gets the deck and the history again after a change of the assembled version. */
  const assembledChanged = useCallback(
    async (text: string) => {
      setPending(null)
      setMessage(text)
      setReload((value) => value + 1)
      await request<Deck>(`/decks/${deck.id}`)
        .then(onChange)
        .catch(() => undefined)
    },
    [deck.id, onChange, request],
  )

  const history = loaded.state === 'done' ? loaded.history : null
  const head = history?.versions[0]
  const canAssemble = owner && !deck.variantOf

  return (
    <section className="panel version-history" aria-labelledby="version-history-title">
      <div className="version-history-header">
        <h2 id="version-history-title">{t('history.title')}</h2>
        <div className="form-actions">
          {canAssemble && history?.assembledVersionId && (
            <>
              <Link to={`/decks/${deck.id}/transition-plan`}>{t('assembled.transitionPlan')}</Link>
              <button type="button" onClick={() => setPending({ action: 'release' })}>
                {t('assembled.release')}
              </button>
            </>
          )}
          {owner && (
            <button type="button" className="button-primary" onClick={() => setPending({ action: 'commit' })}>
              {t('history.commit')}
            </button>
          )}
        </div>
      </div>
      {message && (
        <p className="alert alert-success" role="status">
          {message}
        </p>
      )}
      {loaded.state === 'loading' && <p role="status">{t('history.loading')}</p>}
      {loaded.state === 'error' && <ErrorAlert error={loaded.error} />}
      {history && (
        <>
          <p className="version-state">
            {history.versions.length === 0
              ? t('history.noVersions')
              : history.workingListChanged
                ? t('history.workingListChanged')
                : t('history.workingListSame')}
            {head && history.workingListChanged && (
              <>
                {' '}
                <Link to={compareUrl({ deckId: deck.id, versionId: head.id }, { deckId: deck.id, versionId: null })}>{t('history.compareWorkingList')}</Link>
              </>
            )}
          </p>
          {history.versions.length > 0 && (
            <ol className="version-list" aria-label={t('history.listLabel')}>
              {history.versions.map((version) => (
                <VersionItem
                  key={version.id}
                  deck={deck}
                  version={version}
                  language={language}
                  selected={selection?.version.id === version.id ? selection.view : null}
                  onSelect={(view) => setSelection(selection?.version.id === version.id && selection.view === view ? null : { version, view })}
                  onRestore={() => setPending({ action: 'restore', version })}
                  onRevert={() => setPending({ action: 'revert', version })}
                  onAssemble={canAssemble ? () => setPending({ action: 'assemble', version }) : undefined}
                />
              ))}
            </ol>
          )}
        </>
      )}
      {selection && <VersionView key={`${selection.version.id}-${selection.view}`} deck={deck} version={selection.version} view={selection.view} onClose={() => setSelection(null)} />}
      {pending?.action === 'commit' && <CommitDialog deckId={deck.id} onClose={() => setPending(null)} onCreated={created} />}
      {pending?.action === 'assemble' && (
        <AssembleDialog deckId={deck.id} version={pending.version} onClose={() => setPending(null)} onDone={() => assembledChanged(t('assembled.done', { number: pending.version.number }))} />
      )}
      {pending?.action === 'release' && <ReleaseDialog deckId={deck.id} onClose={() => setPending(null)} onDone={() => assembledChanged(t('assembled.released'))} />}
      {(pending?.action === 'restore' || pending?.action === 'revert') && (
        <ConfirmDialog deckId={deck.id} action={pending.action} version={pending.version} onClose={() => setPending(null)} onCreated={created} />
      )}
    </section>
  )
}

interface VersionItemProps {
  deck: Deck
  version: DeckVersion
  language: string
  selected: 'lines' | 'changes' | null
  onSelect(view: 'lines' | 'changes'): void
  onRestore(): void
  onRevert(): void
  /** Marks the version as the assembled version. Undefined when the user cannot do it. */
  onAssemble?(): void
}

function VersionItem({ deck, version, language, selected, onSelect, onRestore, onRevert, onAssemble }: VersionItemProps) {
  const { t } = useTranslation('versions')
  const number = version.number
  const parentId = version.parentIds[0]
  return (
    <li className="version-item">
      <div className="version-item-header">
        <strong>{t('history.versionName', { number })}</strong>
        <span className={`badge version-kind version-kind-${versionKindKey(version.kind)}`}>{t(`kinds.${versionKindKey(version.kind)}`)}</span>
        {version.assembled && <span className="badge version-assembled">{t('history.assembled')}</span>}
        <time dateTime={version.createdAt}>{formatVersionDate(version.createdAt, language)}</time>
        <span className="version-count">{t('history.cardCount', { count: version.cardCount })}</span>
      </div>
      <p className="version-message">{version.message}</p>
      <div className="version-actions">
        <button type="button" className="button-small" aria-pressed={selected === 'lines'} aria-label={t('history.viewLabel', { number })} onClick={() => onSelect('lines')}>
          {t('history.view')}
        </button>
        <button type="button" className="button-small" aria-pressed={selected === 'changes'} aria-label={t('history.changesLabel', { number })} onClick={() => onSelect('changes')}>
          {t('history.changes')}
        </button>
        {parentId && (
          <Link to={compareUrl({ deckId: deck.id, versionId: parentId }, { deckId: deck.id, versionId: version.id })} aria-label={t('history.compareParentLabel', { number })}>
            {t('history.compareParent')}
          </Link>
        )}
        <Link to={compareUrl({ deckId: deck.id, versionId: version.id }, { deckId: deck.id, versionId: null })} aria-label={t('history.compareWorkingLabel', { number })}>
          {t('history.compareWorking')}
        </Link>
        {deck.isOwner && (
          <button type="button" className="button-small" aria-label={t('history.restoreLabel', { number })} onClick={onRestore}>
            {t('history.restore')}
          </button>
        )}
        {onAssemble && !version.assembled && (
          <button type="button" className="button-small" aria-label={t('assembled.markLabel', { number })} onClick={onAssemble}>
            {t('assembled.mark')}
          </button>
        )}
        {deck.isOwner && version.kind === 'merge' && (
          <button type="button" className="button-small button-danger" aria-label={t('history.revertLabel', { number })} onClick={onRevert}>
            {t('history.revert')}
          </button>
        )}
      </div>
    </li>
  )
}

type Detail = { state: 'loading' } | { state: 'error'; error: unknown } | { state: 'lines'; detail: DeckVersionDetail } | { state: 'changes'; changes: ListChanges }

/** The frozen lines of a version, or its changes against its parent. */
function VersionView({ deck, version, view, onClose }: { deck: Deck; version: DeckVersion; view: 'lines' | 'changes'; onClose(): void }) {
  const { t } = useTranslation('versions')
  const { t: td } = useTranslation('decks')
  const { request } = useAuth()
  const id = useId()
  const heading = useRef<HTMLHeadingElement>(null)
  const [detail, setDetail] = useState<Detail>({ state: 'loading' })

  useEffect(() => {
    heading.current?.focus()
    const path = `/decks/${deck.id}/versions/${version.id}`
    const load =
      view === 'lines'
        ? request<DeckVersionDetail>(path).then((value): Detail => ({ state: 'lines', detail: value }))
        : request<ListChanges>(`${path}/changes`).then((value): Detail => ({ state: 'changes', changes: value }))
    load.then(setDetail).catch((error: unknown) => setDetail({ state: 'error', error }))
  }, [deck.id, request, version.id, view])

  return (
    <section className="version-view" aria-labelledby={`${id}-title`}>
      <div className="version-history-header">
        <h3 id={`${id}-title`} ref={heading} tabIndex={-1}>
          {view === 'lines' ? t('view.linesTitle', { number: version.number }) : t('view.changesTitle', { number: version.number })}
        </h3>
        <button type="button" className="button-small" onClick={onClose}>
          {t('view.close')}
        </button>
      </div>
      {detail.state === 'loading' && <p role="status">{t('view.loading')}</p>}
      {detail.state === 'error' && <ErrorAlert error={detail.error} />}
      {detail.state === 'changes' && <ListChangesView changes={detail.changes} beforeLabel={t('view.parent')} afterLabel={t('view.thisVersion')} />}
      {detail.state === 'lines' && (
        <div className="version-zones">
          {zones.map((zone) => {
            const lines = detail.detail.lines
              .filter((line) => zoneOf(line.zone) === zone)
              .sort((left, right) => (detail.detail.cards[left.oracleId]?.name ?? '').localeCompare(detail.detail.cards[right.oracleId]?.name ?? ''))
            if (lines.length === 0) {
              return null
            }
            const count = lines.reduce((total, line) => total + line.quantity, 0)
            return (
              <section key={zone} aria-labelledby={`${id}-${zone}`}>
                <h4 id={`${id}-${zone}`}>
                  {td(`zones.${zone}`)} <span className="zone-count">({count})</span>
                </h4>
                <ul className="version-lines">
                  {lines.map((line, index) => {
                    const printing = line.printingId ? detail.detail.printings[line.printingId] : undefined
                    return (
                      <li key={`${line.oracleId}-${line.printingId ?? ''}-${line.finish ?? ''}-${index}`}>
                        <span className="version-line-quantity">{line.quantity}</span>{' '}
                        <Link to={`/cards/${line.oracleId}`}>{detail.detail.cards[line.oracleId]?.name ?? line.oracleId}</Link>
                        {printing && <span className="badge">{t('changes.printing', { set: printing.setCode.toUpperCase(), number: printing.collectorNumber })}</span>}
                        {line.finish && line.finish !== 'nonfoil' && <span className="badge">{td(`finishes.${line.finish === 'etched' ? 'etched' : 'foil'}`)}</span>}
                        {line.note && <span className="version-line-note">{line.note}</span>}
                      </li>
                    )
                  })}
                </ul>
              </section>
            )
          })}
          {detail.detail.lines.length === 0 && <p className="field-hint">{t('view.empty')}</p>}
        </div>
      )}
    </section>
  )
}

interface CommitDialogProps {
  deckId: string
  onClose(): void
  onCreated(version: DeckVersion): Promise<void>
}

/** The dialog of a commit: the message of the new version. */
function CommitDialog({ deckId, onClose, onCreated }: CommitDialogProps) {
  const { t } = useTranslation('versions')
  const { request } = useAuth()
  const id = useId()
  const [text, setText] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const noChanges = error instanceof ApiError && error.code === noChangesCode
  const fieldCode = error instanceof ApiError ? error.fieldCode('message') : undefined

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await onCreated(await request<DeckVersion>(`/decks/${deckId}/versions`, { method: 'POST', body: { message: text.trim() } }))
    } catch (caught) {
      setError(caught)
      setBusy(false)
    }
  }

  return (
    <Dialog title={t('commit.title')} onClose={onClose}>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <p className="field-hint">{t('commit.help')}</p>
        <div className="field">
          <label htmlFor={`${id}-message`}>{t('commit.message')}</label>
          <textarea
            id={`${id}-message`}
            data-autofocus
            rows={3}
            maxLength={maxMessageLength}
            required
            value={text}
            aria-invalid={fieldCode ? true : undefined}
            aria-describedby={fieldCode ? `${id}-error` : undefined}
            onChange={(event) => setText(event.target.value)}
          />
          {fieldCode && (
            <p className="field-error" id={`${id}-error`}>
              {t('commit.messageRequired')}
            </p>
          )}
        </div>
        {noChanges ? (
          <p className="alert alert-warning" role="alert">
            {t('commit.noChanges')}
          </p>
        ) : (
          !fieldCode && <ErrorAlert error={error} />
        )}
        <div className="form-actions">
          <button type="submit" className="button-primary" disabled={busy || text.trim() === ''}>
            {t('commit.submit')}
          </button>
          <button type="button" onClick={onClose}>
            {t('actions.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

interface ConfirmDialogProps {
  deckId: string
  action: 'restore' | 'revert'
  version: DeckVersion
  onClose(): void
  onCreated(version: DeckVersion): Promise<void>
}

/** The dialog that asks the owner before a restore or a revert. */
function ConfirmDialog({ deckId, action, version, onClose, onCreated }: ConfirmDialogProps) {
  const { t } = useTranslation('versions')
  const { request } = useAuth()
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const number = version.number

  async function submit() {
    setError(null)
    setBusy(true)
    try {
      await onCreated(await request<DeckVersion>(`/decks/${deckId}/versions/${version.id}/${action}`, { method: 'POST', body: {} }))
    } catch (caught) {
      setError(caught)
      setBusy(false)
    }
  }

  return (
    <Dialog title={action === 'restore' ? t('restore.title', { number }) : t('revert.title', { number })} onClose={onClose}>
      <p>{action === 'restore' ? t('restore.help', { number }) : t('revert.help', { number })}</p>
      <ErrorAlert error={error} />
      <div className="form-actions">
        <button type="button" className={action === 'revert' ? 'button-danger' : 'button-primary'} disabled={busy} onClick={() => void submit()}>
          {action === 'restore' ? t('restore.submit') : t('revert.submit')}
        </button>
        <button type="button" data-autofocus onClick={onClose}>
          {t('actions.cancel')}
        </button>
      </div>
    </Dialog>
  )
}

interface AssembleDialogProps {
  deckId: string
  version: DeckVersion
  onClose(): void
  onDone(): Promise<void>
}

/** The dialog that asks the owner before a version becomes the assembled version. */
function AssembleDialog({ deckId, version, onClose, onDone }: AssembleDialogProps) {
  const { t } = useTranslation('versions')
  const { request } = useAuth()
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const number = version.number

  async function submit() {
    setError(null)
    setBusy(true)
    try {
      await request(`/decks/${deckId}/assembled`, { method: 'PUT', body: { versionId: version.id } })
      await onDone()
    } catch (caught) {
      setError(caught)
      setBusy(false)
    }
  }

  return (
    <Dialog title={t('assembled.markTitle', { number })} onClose={onClose}>
      <p>{t('assembled.markHelp', { number })}</p>
      <ErrorAlert error={error} />
      <div className="form-actions">
        <button type="button" className="button-primary" disabled={busy} onClick={() => void submit()}>
          {t('assembled.mark')}
        </button>
        <button type="button" data-autofocus onClick={onClose}>
          {t('actions.cancel')}
        </button>
      </div>
    </Dialog>
  )
}

interface ReleaseDialogProps {
  deckId: string
  onClose(): void
  onDone(): Promise<void>
}

/** The dialog of the release of an assembled deck: the owner tells where the cards go. */
function ReleaseDialog({ deckId, onClose, onDone }: ReleaseDialogProps) {
  const { t } = useTranslation('versions')
  const { request } = useAuth()
  const id = useId()
  const [locations, setLocations] = useState<CollectionLocation[]>([])
  const [target, setTarget] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    request<CollectionLocation[]>('/collection/locations')
      .then(setLocations)
      .catch((caught: unknown) => setError(caught))
  }, [request])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await request(`/decks/${deckId}/assembled${target ? `?targetLocationId=${target}` : ''}`, { method: 'DELETE' })
      await onDone()
    } catch (caught) {
      setError(caught)
      setBusy(false)
    }
  }

  const targets = flattenLocations(locations).filter(({ location }) => location.kind !== 'deck')
  return (
    <Dialog title={t('assembled.releaseTitle')} onClose={onClose}>
      <form className="form" onSubmit={(event) => void submit(event)}>
        <p>{t('assembled.releaseHelp')}</p>
        <div className="field">
          <label htmlFor={`${id}-target`}>{t('assembled.target')}</label>
          <select id={`${id}-target`} data-autofocus value={target} onChange={(event) => setTarget(event.target.value)}>
            <option value="">{t('assembled.noTarget')}</option>
            {targets.map(({ location, parent }) => (
              <option key={location.id} value={location.id}>
                {parent ? locationLabel(parent.name, location.name) : location.name}
              </option>
            ))}
          </select>
        </div>
        <ErrorAlert error={error} />
        <div className="form-actions">
          <button type="submit" className="button-danger" disabled={busy}>
            {t('assembled.release')}
          </button>
          <button type="button" onClick={onClose}>
            {t('actions.cancel')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
