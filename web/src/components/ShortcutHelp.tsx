import { Fragment, useEffect, useRef, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { shortcuts } from '../shortcuts/shortcuts'

const focusableSelector = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * The list of the keyboard shortcuts, in a modal dialog.
 * The focus stays in the dialog. Escape closes the dialog, and the focus goes back to the element that had it before.
 */
export function ShortcutHelp({ onClose }: { onClose(): void }) {
  const { t } = useTranslation()
  const dialog = useRef<HTMLDivElement>(null)
  const closeButton = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    closeButton.current?.focus()
    return () => previous?.focus()
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

  return (
    <div className="dialog-backdrop" onClick={onClose}>
      <div
        ref={dialog}
        className="dialog-modal panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="shortcut-help-title"
        aria-describedby="shortcut-help-description"
        onKeyDown={handleKeyDown}
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id="shortcut-help-title">{t('shortcuts.title')}</h2>
        <p id="shortcut-help-description" className="field-hint">
          {t('shortcuts.description')}
        </p>
        <dl className="shortcut-list">
          {shortcuts.map((shortcut) => (
            <div key={shortcut.keys.join(' ')} className="shortcut">
              <dt>
                {shortcut.keys.map((key, index) => (
                  <Fragment key={key}>
                    {index > 0 && ` ${t('shortcuts.then')} `}
                    <kbd>{key}</kbd>
                  </Fragment>
                ))}
              </dt>
              <dd>{t(`shortcuts.actions.${shortcut.description}`)}</dd>
            </div>
          ))}
        </dl>
        <button ref={closeButton} type="button" onClick={onClose}>
          {t('shortcuts.close')}
        </button>
      </div>
    </div>
  )
}
