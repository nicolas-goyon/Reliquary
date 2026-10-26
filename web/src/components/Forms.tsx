import { useId, type InputHTMLAttributes, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { errorCode, errorKey, fieldErrorKey } from '../api/errors'

interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string
  /** The name of the field in the API, to show its error. */
  field?: string
  error?: unknown
  hint?: ReactNode
}

/** A labeled input that shows the error of its field. */
export function TextField({ label, field, error, hint, ...input }: TextFieldProps) {
  const { t } = useTranslation()
  const id = useId()
  const code = field && error instanceof ApiError ? error.fieldCode(field) : undefined
  const describedBy = [hint ? `${id}-hint` : null, code ? `${id}-error` : null].filter(Boolean).join(' ') || undefined
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input id={id} aria-invalid={code ? true : undefined} aria-describedby={describedBy} {...input} />
      {hint && (
        <p className="field-hint" id={`${id}-hint`}>
          {hint}
        </p>
      )}
      {code && (
        <p className="field-error" id={`${id}-error`}>
          {t(fieldErrorKey(code))}
        </p>
      )}
    </div>
  )
}

/** Shows an error of the API in the language of the user. */
export function ErrorAlert({ error }: { error: unknown }) {
  const { t } = useTranslation()
  if (!error) {
    return null
  }
  return (
    <p className="alert alert-error" role="alert">
      {t(errorKey(errorCode(error)))}
    </p>
  )
}

/** Shows a message of success. */
export function SuccessAlert({ children }: { children: ReactNode }) {
  return (
    <p className="alert alert-success" role="status">
      {children}
    </p>
  )
}
