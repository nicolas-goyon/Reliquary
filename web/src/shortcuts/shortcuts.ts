import { useEffect } from 'react'
import { navigate } from '../router/navigation'

/**
 * The keyboard shortcuts of the web app. See docs/development/web-app.md.
 *
 * A page marks the element of a shortcut with the attribute data-shortcut, for example data-shortcut="add-card".
 * The shortcuts do not work when the focus is in a field, or when the user holds Ctrl, Alt or Meta.
 */

/** The elements that a shortcut can find on the page. */
export type ShortcutTarget = 'search' | 'new-deck' | 'add-card'

export type ShortcutAction =
  | { type: 'navigate'; to: string }
  | { type: 'focus'; target: ShortcutTarget; fallback?: string }
  | { type: 'click'; target: ShortcutTarget }
  | { type: 'help' }

export interface Shortcut {
  /** The keys, one after the other. For example ["g", "d"]. */
  keys: readonly string[]
  action: ShortcutAction
  /** The key of the text in the namespace "common", under "shortcuts.actions". */
  description: 'search' | 'home' | 'cards' | 'decks' | 'explore' | 'newDeck' | 'addCard' | 'help'
}

export const shortcuts: readonly Shortcut[] = [
  { keys: ['/'], action: { type: 'focus', target: 'search', fallback: '/cards' }, description: 'search' },
  { keys: ['g', 'h'], action: { type: 'navigate', to: '/' }, description: 'home' },
  { keys: ['g', 'c'], action: { type: 'navigate', to: '/cards' }, description: 'cards' },
  { keys: ['g', 'd'], action: { type: 'navigate', to: '/decks' }, description: 'decks' },
  { keys: ['g', 'e'], action: { type: 'navigate', to: '/explore' }, description: 'explore' },
  { keys: ['n'], action: { type: 'click', target: 'new-deck' }, description: 'newDeck' },
  { keys: ['a'], action: { type: 'focus', target: 'add-card' }, description: 'addCard' },
  { keys: ['?'], action: { type: 'help' }, description: 'help' },
]

/** The maximum time between two keys of one shortcut, in milliseconds. */
export const sequenceTimeout = 1500

/** Finds the element of a shortcut on the page. */
export function shortcutElement(target: ShortcutTarget): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-shortcut="${target}"]`)
}

/** Tells if the user types in this element. A shortcut must not start there. */
export function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false
  }
  return target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement || target.isContentEditable
}

/** Tells if the user holds Ctrl, Alt or Meta. The shortcuts accept Shift, because some keyboards need it for "/" and "?". */
export function hasModifier(event: Pick<KeyboardEvent, 'ctrlKey' | 'altKey' | 'metaKey'>): boolean {
  return event.ctrlKey || event.altKey || event.metaKey
}

/** Finds the shortcut of a sequence of keys. */
export class ShortcutMatcher {
  private pending: string[] = []
  private lastTime = 0
  private readonly list: readonly Shortcut[]

  constructor(list: readonly Shortcut[] = shortcuts) {
    this.list = list
  }

  /**
   * Adds a key to the sequence.
   * @returns The shortcut, "pending" when the key starts a longer shortcut, or null.
   */
  press(key: string, time: number): Shortcut | 'pending' | null {
    const previous = time - this.lastTime <= sequenceTimeout ? this.pending : []
    this.lastTime = time
    for (const sequence of previous.length > 0 ? [[...previous, key], [key]] : [[key]]) {
      const match = this.list.find((shortcut) => sameKeys(shortcut.keys, sequence))
      if (match) {
        this.pending = []
        return match
      }
      if (this.list.some((shortcut) => shortcut.keys.length > sequence.length && sameKeys(shortcut.keys.slice(0, sequence.length), sequence))) {
        this.pending = sequence
        return 'pending'
      }
    }
    this.pending = []
    return null
  }
}

function sameKeys(left: readonly string[], right: readonly string[]): boolean {
  return left.length === right.length && left.every((key, index) => key === right[index])
}

let pendingFocus: ShortcutTarget | null = null

/**
 * Gives the element that a shortcut must focus after a page change, and clears it.
 * The shell calls this function after it moves the focus to the new page.
 */
export function takePendingFocus(): ShortcutTarget | null {
  const target = pendingFocus
  pendingFocus = null
  return target
}

/** Runs the action of a shortcut. */
export function runShortcut(action: ShortcutAction, openHelp: () => void): void {
  switch (action.type) {
    case 'navigate':
      navigate(action.to)
      return
    case 'focus': {
      const element = shortcutElement(action.target)
      if (element) {
        element.focus()
      } else if (action.fallback) {
        pendingFocus = action.target
        navigate(action.fallback)
      }
      return
    }
    case 'click':
      shortcutElement(action.target)?.click()
      return
    case 'help':
      openHelp()
  }
}

/**
 * Listens to the keyboard shortcuts on the document.
 * @param enabled False stops the shortcuts, for example while a dialog is open.
 */
export function useShortcuts(enabled: boolean, openHelp: () => void): void {
  useEffect(() => {
    if (!enabled) {
      return
    }
    const matcher = new ShortcutMatcher()
    function handleKeyDown(event: KeyboardEvent) {
      if (event.defaultPrevented || event.isComposing || hasModifier(event) || isTypingTarget(event.target)) {
        return
      }
      const result = matcher.press(event.key, performance.now())
      if (result === null) {
        return
      }
      // The key must not also type a character, for example in a field that the shortcut focuses.
      event.preventDefault()
      if (result !== 'pending') {
        runShortcut(result.action, openHelp)
      }
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [enabled, openHelp])
}
