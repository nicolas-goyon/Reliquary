import { useSyncExternalStore } from 'react'

/**
 * A small router on the History API. The application has few pages, so it does not need a router library.
 */
const navigationEvent = 'reliquary:navigate'

function subscribe(onChange: () => void): () => void {
  window.addEventListener('popstate', onChange)
  window.addEventListener(navigationEvent, onChange)
  return () => {
    window.removeEventListener('popstate', onChange)
    window.removeEventListener(navigationEvent, onChange)
  }
}

function currentLocation(): string {
  return window.location.pathname + window.location.search
}

/** Gets the path and the query of the current page, and renders again when they change. */
export function useLocation(): URL {
  const location = useSyncExternalStore(subscribe, currentLocation)
  return new URL(location, window.location.origin)
}

/** Opens a page of the application with no full reload. */
export function navigate(to: string, options: { replace?: boolean } = {}): void {
  if (options.replace) {
    window.history.replaceState(null, '', to)
  } else {
    window.history.pushState(null, '', to)
  }
  window.dispatchEvent(new Event(navigationEvent))
  window.scrollTo?.(0, 0)
}

/**
 * Accepts only a local path, so a link cannot send the user to another site.
 * @returns The path, or "/" when the value is not a local path.
 */
export function safeReturnUrl(value: string | null | undefined): string {
  return value && value.startsWith('/') && !value.startsWith('//') && !value.includes('\\') ? value : '/'
}
