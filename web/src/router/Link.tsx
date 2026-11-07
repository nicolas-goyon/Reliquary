import type { AnchorHTMLAttributes, MouseEvent } from 'react'
import { navigate, useLocation } from './navigation'

interface LinkProps extends AnchorHTMLAttributes<HTMLAnchorElement> {
  to: string
}

/** A link to a page of the application. It opens the page with no full reload. */
export function Link({ to, onClick, ...props }: LinkProps) {
  const location = useLocation()

  function handleClick(event: MouseEvent<HTMLAnchorElement>) {
    onClick?.(event)
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
      return
    }
    event.preventDefault()
    navigate(to)
  }

  return <a href={to} onClick={handleClick} aria-current={location.pathname === to ? 'page' : undefined} {...props} />
}
