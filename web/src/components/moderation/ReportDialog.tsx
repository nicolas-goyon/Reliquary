import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../api/client'
import { maxCommentLength, reportReasons, type ReportItemType, type ReportReason } from '../../api/moderation'
import { useAuth } from '../../auth/session'
import { ErrorAlert, SuccessAlert } from '../Forms'

interface ReportTarget {
  itemType: ReportItemType
  itemId: string
}

/**
 * The "Report" button of a public deck or of an author profile, with its dialog.
 * It shows nothing to a visitor with no account. The page does not show it to the owner.
 */
export function ReportButton({ itemType, itemId }: ReportTarget) {
  const { t } = useTranslation('moderation')
  const { state } = useAuth()
  const [open, setOpen] = useState(false)
  const [sent, setSent] = useState(false)

  if (state.status !== 'authenticated') {
    return null
  }

  return (
    <>
      <button type="button" onClick={() => setOpen(true)} disabled={sent}>
        {t('report.button')}
      </button>
      {sent && <SuccessAlert>{t('report.sent')}</SuccessAlert>}
      {open && (
        <ReportDialog
          itemType={itemType}
          itemId={itemId}
          onClose={() => setOpen(false)}
          onSent={() => {
            setOpen(false)
            setSent(true)
          }}
        />
      )}
    </>
  )
}

/** The dialog of a report: the reason and an optional comment. */
export function ReportDialog({ itemType, itemId, onClose, onSent }: ReportTarget & { onClose(): void; onSent(): void }) {
  const { t } = useTranslation('moderation')
  const { request } = useAuth()
  const id = useId()
  const [reason, setReason] = useState<ReportReason | ''>('')
  const [comment, setComment] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [sending, setSending] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setSending(true)
    try {
      await request('/reports', { method: 'POST', body: { itemType, itemId, reason, comment: comment.trim() } })
      onSent()
    } catch (caught) {
      setError(caught)
    } finally {
      setSending(false)
    }
  }

  const tooMany = error instanceof ApiError && error.code === 'rate_limited'
  return (
    <div className="dialog" role="dialog" aria-labelledby={`${id}-title`}>
      <form className="form panel" onSubmit={(event) => void handleSubmit(event)} noValidate>
        <h2 id={`${id}-title`}>{t(`report.title.${itemType}`)}</h2>
        <p>{t('report.description')}</p>
        <fieldset className="field report-reasons">
          <legend>{t('report.reason')}</legend>
          {reportReasons.map((value) => (
            <label key={value} className="report-reason">
              <input type="radio" name={`${id}-reason`} value={value} checked={reason === value} onChange={() => setReason(value)} required />{' '}
              {t(`report.reasons.${value}`)}
            </label>
          ))}
        </fieldset>
        <div className="field">
          <label htmlFor={`${id}-comment`}>{t('report.comment')}</label>
          <textarea
            id={`${id}-comment`}
            value={comment}
            maxLength={maxCommentLength}
            rows={4}
            aria-describedby={`${id}-comment-hint`}
            onChange={(event) => setComment(event.target.value)}
          />
          <p className="field-hint" id={`${id}-comment-hint`}>
            {t('report.commentHint', { max: maxCommentLength })}
          </p>
        </div>
        {tooMany ? (
          <p className="alert alert-error" role="alert">
            {t('report.tooMany')}
          </p>
        ) : (
          <ErrorAlert error={error} />
        )}
        <div className="form-actions">
          <button type="submit" className="button-primary" disabled={!reason || sending}>
            {t('report.submit')}
          </button>
          <button type="button" onClick={onClose}>
            {t('report.cancel')}
          </button>
        </div>
      </form>
    </div>
  )
}
