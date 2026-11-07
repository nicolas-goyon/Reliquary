import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode } from 'react'
import './versions.css'

const focusableSelector = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

interface DialogProps {
  title: string
  /** Closes the dialog. The Escape key also calls it. */
  onClose(): void
  /** A wide dialog for tables. */
  wide?: boolean
  children: ReactNode
}

/**
 * A modal dialog with its title as its name.
 * The dialog moves the focus into it, keeps the Tab key in it, closes with the Escape key,
 * and gives the focus back to the previous element when it closes.
 */
export function Dialog({ title, onClose, wide = false, children }: DialogProps) {
  const id = useId()
  const dialog = useRef<HTMLDivElement>(null)
  const close = useRef(onClose)

  useEffect(() => {
    close.current = onClose
  })

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const element = dialog.current
    const first = element?.querySelector<HTMLElement>('[data-autofocus]') ?? element?.querySelector<HTMLElement>(focusableSelector)
    ;(first ?? element)?.focus()
    return () => {
      if (previous?.isConnected) {
        previous.focus()
      }
    }
  }, [])

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      event.stopPropagation()
      close.current()
      return
    }
    if (event.key !== 'Tab' || !dialog.current) {
      return
    }
    const focusable = [...dialog.current.querySelectorAll<HTMLElement>(focusableSelector)]
    if (focusable.length === 0) {
      event.preventDefault()
      return
    }
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
    <div className="version-dialog-backdrop">
      <div
        ref={dialog}
        className={wide ? 'version-dialog version-dialog-wide panel' : 'version-dialog panel'}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${id}-title`}
        tabIndex={-1}
        onKeyDown={handleKeyDown}
      >
        <h2 id={`${id}-title`}>{title}</h2>
        {children}
      </div>
    </div>
  )
}
