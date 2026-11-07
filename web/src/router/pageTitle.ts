import { useEffect, type RefObject } from 'react'

/** Gives the text of a heading without the text of its buttons, for example "Rename" in the title of a deck. */
function headingText(heading: HTMLElement): string {
  const copy = heading.cloneNode(true) as HTMLElement
  for (const button of copy.querySelectorAll('button')) {
    button.remove()
  }
  return (copy.textContent ?? '').replace(/\s+/g, ' ').trim()
}

/**
 * Keeps the title of the document equal to the level 1 heading of the page, followed by the name of the application.
 * A screen reader reads the title when the page changes, and the browser shows it in the tab and in the history.
 */
export function usePageTitle(main: RefObject<HTMLElement | null>, appName: string): void {
  useEffect(() => {
    const element = main.current
    if (!element) {
      return
    }
    const container = element
    function update() {
      const heading = container.querySelector('h1')
      const text = heading ? headingText(heading) : ''
      const title = text && text !== appName ? `${text} · ${appName}` : appName
      if (document.title !== title) {
        document.title = title
      }
    }
    update()
    const observer = new MutationObserver(update)
    observer.observe(container, { childList: true, subtree: true, characterData: true })
    return () => observer.disconnect()
  }, [main, appName])
}
