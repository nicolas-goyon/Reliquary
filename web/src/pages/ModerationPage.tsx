import { useCallback, useEffect, useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Page } from '../api/cards'
import {
  itemPath,
  maxCommentLength,
  reportStatuses,
  type HiddenItem,
  type ModerationLogEntry,
  type ModerationReport,
  type ModerationUser,
  type ReportItemType,
  type ReportStatus,
} from '../api/moderation'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import i18n from '../i18n'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'

const sections = ['reports', 'hidden', 'log'] as const
type Section = (typeof sections)[number]

/** A list of the API with pages: the items, and a function that gets the next page. */
function usePagedList<T>(path: string, version: number) {
  const { request } = useAuth()
  const [items, setItems] = useState<T[]>([])
  const [next, setNext] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    request<Page<T>>(path)
      .then((page) => {
        setError(null)
        setItems(page.items)
        setNext(page.nextCursor)
      })
      .catch((caught: unknown) => setError(caught))
  }, [path, request, version])

  const more = useCallback(async () => {
    if (!next) {
      return
    }
    try {
      const page = await request<Page<T>>(`${path}${path.includes('?') ? '&' : '?'}cursor=${encodeURIComponent(next)}`)
      setItems((current) => [...current, ...page.items])
      setNext(page.nextCursor)
    } catch (caught) {
      setError(caught)
    }
  }, [next, path, request])

  return { items, next, error, more }
}

/** The moderation page of the administrators: the reports, the hidden content and the moderation log. */
export function ModerationPage() {
  const { t } = useTranslation('moderation')
  const { state, request } = useAuth()
  const id = useId()
  const [section, setSection] = useState<Section>('reports')
  const [reason, setReason] = useState('')
  const [version, setVersion] = useState(0)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent('/admin/moderation')}`, { replace: true })
    }
  }, [state.status])

  if (state.status !== 'authenticated') {
    return null
  }

  if (!state.account.isAdministrator) {
    return (
      <section className="page">
        <h1>{t('admin.title')}</h1>
        <p className="alert alert-error" role="alert">
          {t('admin.forbidden')}
        </p>
      </section>
    )
  }

  /** Sends an action of the administrator, then reads the lists again. */
  async function act(path: string, method: 'POST' | 'PUT' | 'DELETE') {
    setError(null)
    const text = reason.trim()
    try {
      if (method === 'DELETE') {
        await request(text ? `${path}?reason=${encodeURIComponent(text)}` : path, { method })
      } else {
        await request(path, { method, body: { reason: text } })
      }
      setReason('')
      setVersion((current) => current + 1)
    } catch (caught) {
      setError(caught)
    }
  }

  const actions: Actions = {
    hide: (itemType, itemId) => act(`/moderation/hidden/${itemType}/${itemId}`, 'PUT'),
    show: (itemType, itemId) => act(`/moderation/hidden/${itemType}/${itemId}`, 'DELETE'),
    close: (reportId, status) => act(`/moderation/reports/${reportId}/${status === 'resolved' ? 'resolve' : 'dismiss'}`, 'POST'),
  }

  return (
    <section className="page page-wide moderation-page">
      <h1>{t('admin.title')}</h1>
      <div className="field">
        <label htmlFor={`${id}-reason`}>{t('admin.actionReason')}</label>
        <input
          id={`${id}-reason`}
          value={reason}
          maxLength={maxCommentLength}
          aria-describedby={`${id}-reason-hint`}
          onChange={(event) => setReason(event.target.value)}
        />
        <p className="field-hint" id={`${id}-reason-hint`}>
          {t('admin.actionReasonHint')}
        </p>
      </div>
      <ErrorAlert error={error} />
      <div className="tab-list" role="tablist" aria-label={t('admin.title')}>
        {sections.map((value) => (
          <button
            key={value}
            type="button"
            role="tab"
            id={`${id}-tab-${value}`}
            className="tab"
            aria-selected={section === value}
            aria-controls={`${id}-panel`}
            onClick={() => setSection(value)}
          >
            {t(`admin.${value}`)}
          </button>
        ))}
      </div>
      <div className="tab-panel" role="tabpanel" id={`${id}-panel`} aria-labelledby={`${id}-tab-${section}`}>
        {section === 'reports' && <ReportList version={version} actions={actions} />}
        {section === 'hidden' && <HiddenList version={version} actions={actions} />}
        {section === 'log' && <LogList version={version} />}
      </div>
    </section>
  )
}

interface Actions {
  hide(itemType: ReportItemType, itemId: string): Promise<void>
  show(itemType: ReportItemType, itemId: string): Promise<void>
  close(reportId: string, status: 'resolved' | 'dismissed'): Promise<void>
}

function dateFormat() {
  return new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: 'medium', timeStyle: 'short' })
}

function UserName({ user }: { user: ModerationUser | null }) {
  const { t } = useTranslation('moderation')
  return <>{user?.displayName || t('admin.deletedAccount')}</>
}

function ItemCell({ itemType, itemId, itemName, hidden }: { itemType: ReportItemType; itemId: string | null; itemName: string; hidden?: boolean }) {
  const { t } = useTranslation('moderation')
  const name = itemName || t('admin.deletedAccount')
  return (
    <>
      <span className="badge">{t(`admin.itemTypes.${itemType}`)}</span> {itemId ? <Link to={itemPath(itemType, itemId)}>{name}</Link> : name}
      {hidden && <span className="badge badge-category">{t('admin.hiddenBadge')}</span>}
    </>
  )
}

function MoreButton({ next, more }: { next: string | null; more(): Promise<void> }) {
  const { t } = useTranslation('moderation')
  if (!next) {
    return null
  }
  return (
    <button type="button" onClick={() => void more()}>
      {t('admin.more')}
    </button>
  )
}

function ReportList({ version, actions }: { version: number; actions: Actions }) {
  const { t } = useTranslation('moderation')
  const id = useId()
  const [status, setStatus] = useState<ReportStatus>('open')
  const { items, next, error, more } = usePagedList<ModerationReport>(`/moderation/reports?status=${status}`, version)
  const dates = dateFormat()

  return (
    <>
      <div className="field">
        <label htmlFor={`${id}-status`}>{t('admin.statusFilter')}</label>
        <select id={`${id}-status`} value={status} onChange={(event) => setStatus(event.target.value as ReportStatus)}>
          {reportStatuses.map((value) => (
            <option key={value} value={value}>
              {t(`admin.status.${value}`)}
            </option>
          ))}
        </select>
      </div>
      <ErrorAlert error={error} />
      {items.length === 0 ? (
        <p>{t('admin.noReports')}</p>
      ) : (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t('admin.columns.item')}</th>
                <th scope="col">{t('admin.columns.owner')}</th>
                <th scope="col">{t('admin.columns.report')}</th>
                <th scope="col">{t('admin.columns.date')}</th>
                {status === 'open' && <th scope="col">{t('admin.columns.actions')}</th>}
              </tr>
            </thead>
            <tbody>
              {items.map((report) => (
                <tr key={report.id}>
                  <td>
                    <ItemCell itemType={report.itemType} itemId={report.itemId} itemName={report.itemName} hidden={report.itemHidden} />
                  </td>
                  <td>
                    <UserName user={report.owner} />
                  </td>
                  <td>
                    <strong>{t(`report.reasons.${report.reason}`)}</strong>
                    {report.comment && <p className="moderation-comment">{report.comment}</p>}
                    <p className="field-hint">{t('admin.reportedBy', { name: report.reporter.displayName || t('admin.deletedAccount') })}</p>
                  </td>
                  <td>{dates.format(new Date(report.createdAt))}</td>
                  {status === 'open' && (
                    <td>
                      <div className="moderation-actions">
                        {report.itemHidden ? (
                          <button type="button" onClick={() => void actions.show(report.itemType, report.itemId)}>
                            {t('admin.show')}
                          </button>
                        ) : (
                          <button type="button" className="button-danger" onClick={() => void actions.hide(report.itemType, report.itemId)}>
                            {t('admin.hide')}
                          </button>
                        )}
                        <button type="button" onClick={() => void actions.close(report.id, 'resolved')}>
                          {t('admin.resolve')}
                        </button>
                        <button type="button" onClick={() => void actions.close(report.id, 'dismissed')}>
                          {t('admin.dismiss')}
                        </button>
                      </div>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <MoreButton next={next} more={more} />
    </>
  )
}

function HiddenList({ version, actions }: { version: number; actions: Actions }) {
  const { t } = useTranslation('moderation')
  const { items, next, error, more } = usePagedList<HiddenItem>('/moderation/hidden', version)
  const dates = dateFormat()

  return (
    <>
      <ErrorAlert error={error} />
      {items.length === 0 ? (
        <p>{t('admin.noHidden')}</p>
      ) : (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t('admin.columns.item')}</th>
                <th scope="col">{t('admin.columns.owner')}</th>
                <th scope="col">{t('admin.columns.reason')}</th>
                <th scope="col">{t('admin.columns.date')}</th>
                <th scope="col">{t('admin.columns.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={`${item.itemType}-${item.itemId}`}>
                  <td>
                    <ItemCell itemType={item.itemType} itemId={item.itemId} itemName={item.itemName} />
                  </td>
                  <td>
                    <UserName user={item.owner} />
                  </td>
                  <td>{item.reason}</td>
                  <td>{dates.format(new Date(item.hiddenAt))}</td>
                  <td>
                    <button type="button" onClick={() => void actions.show(item.itemType, item.itemId)}>
                      {t('admin.show')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <MoreButton next={next} more={more} />
    </>
  )
}

function LogList({ version }: { version: number }) {
  const { t } = useTranslation('moderation')
  const { items, next, error, more } = usePagedList<ModerationLogEntry>('/moderation/log', version)
  const dates = dateFormat()

  return (
    <>
      <ErrorAlert error={error} />
      {items.length === 0 ? (
        <p>{t('admin.noLog')}</p>
      ) : (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{t('admin.columns.date')}</th>
                <th scope="col">{t('admin.columns.moderator')}</th>
                <th scope="col">{t('admin.columns.action')}</th>
                <th scope="col">{t('admin.columns.item')}</th>
                <th scope="col">{t('admin.columns.reason')}</th>
              </tr>
            </thead>
            <tbody>
              {items.map((entry) => (
                <tr key={entry.id}>
                  <td>{dates.format(new Date(entry.createdAt))}</td>
                  <td>
                    <UserName user={entry.moderator} />
                  </td>
                  <td>{t(`admin.actions.${entry.action}`)}</td>
                  <td>
                    <ItemCell itemType={entry.itemType} itemId={entry.itemId} itemName={entry.itemName} />
                  </td>
                  <td>{entry.reason}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <MoreButton next={next} more={more} />
    </>
  )
}
