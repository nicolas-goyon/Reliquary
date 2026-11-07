import { useEffect, useId, useRef, useState, type InputHTMLAttributes, type KeyboardEvent } from 'react'

/** A suggestion of a combobox. */
export interface Suggestion<T> {
  key: string
  label: string
  detail?: string
  value: T
}

interface ComboboxProps<T> extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'onSelect' | 'id'> {
  label: string
  value: string
  onChange(value: string): void
  /** Gets the suggestions for the text. The signal stops an old request. */
  suggest(text: string, signal: AbortSignal): Promise<Suggestion<T>[]>
  onSelect(suggestion: Suggestion<T>): void
  /** Hides the label, for a field with a visible title. */
  hideLabel?: boolean
}

const delay = 150

/**
 * A text field with a list of suggestions. It follows the combobox pattern of WAI-ARIA:
 * the arrow keys move in the list, Enter selects, and Escape closes the list.
 */
export function Combobox<T>({ label, value, onChange, suggest, onSelect, hideLabel, onKeyDown, ...input }: ComboboxProps<T>) {
  const id = useId()
  const [suggestions, setSuggestions] = useState<Suggestion<T>[]>([])
  const [active, setActive] = useState(-1)
  const [open, setOpen] = useState(false)
  const suggestRef = useRef(suggest)

  useEffect(() => {
    suggestRef.current = suggest
  }, [suggest])

  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      suggestRef.current(value, controller.signal)
        .then((items) => {
          if (!controller.signal.aborted) {
            setSuggestions(items)
            setActive(-1)
          }
        })
        .catch(() => undefined)
    }, delay)
    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [value])

  function select(suggestion: Suggestion<T>) {
    setOpen(false)
    setActive(-1)
    onSelect(suggestion)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    const expanded = open && suggestions.length > 0
    if (event.key === 'ArrowDown' && suggestions.length > 0) {
      event.preventDefault()
      setOpen(true)
      setActive((current) => (current + 1) % suggestions.length)
    } else if (event.key === 'ArrowUp' && expanded) {
      event.preventDefault()
      setActive((current) => (current <= 0 ? suggestions.length - 1 : current - 1))
    } else if (event.key === 'Enter' && expanded && active >= 0) {
      event.preventDefault()
      select(suggestions[active])
    } else if (event.key === 'Escape' && expanded) {
      event.preventDefault()
      setOpen(false)
    } else {
      onKeyDown?.(event)
    }
  }

  const listId = `${id}-list`
  const expanded = open && suggestions.length > 0
  return (
    <div className="combobox">
      <label htmlFor={id} className={hideLabel ? 'visually-hidden' : undefined}>
        {label}
      </label>
      <input
        id={id}
        role="combobox"
        aria-autocomplete="list"
        aria-expanded={expanded}
        aria-controls={listId}
        aria-activedescendant={expanded && active >= 0 ? `${id}-option-${active}` : undefined}
        autoComplete="off"
        value={value}
        onChange={(event) => {
          onChange(event.target.value)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => window.setTimeout(() => setOpen(false), 150)}
        onKeyDown={handleKeyDown}
        {...input}
      />
      <ul id={listId} role="listbox" className="combobox-list" hidden={!expanded}>
        {suggestions.map((suggestion, index) => (
          <li
            key={suggestion.key}
            id={`${id}-option-${index}`}
            role="option"
            aria-selected={index === active}
            className={index === active ? 'combobox-option active' : 'combobox-option'}
            onMouseDown={(event) => {
              event.preventDefault()
              select(suggestion)
            }}
          >
            <span>{suggestion.label}</span>
            {suggestion.detail && <span className="combobox-detail">{suggestion.detail}</span>}
          </li>
        ))}
      </ul>
    </div>
  )
}
