import { useId, useRef, useState, type KeyboardEvent } from 'react'
import { tabTitle } from '../../api/analysis'
import { useSlotContributions, type SlotContexts } from '../../slots/registry'
import { SlotItem } from '../../slots/Slot'

interface AnalysisTabsProps {
  context: SlotContexts['analysis.tabs']
  /** The ID of the heading that names the tab list. */
  labelledBy: string
}

/**
 * The tabs of the deck analysis. Each contribution of the slot "analysis.tabs" is one tab, so the extensions can add tabs.
 * The tab list follows the WAI-ARIA tabs pattern: the arrow keys, Home and End move between the tabs.
 */
export function AnalysisTabs({ context, labelledBy }: AnalysisTabsProps) {
  const id = useId()
  const contributions = useSlotContributions('analysis.tabs')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const tabs = useRef(new Map<string, HTMLButtonElement>())

  const visible = contributions
    .map((contribution) => ({ contribution, title: tabTitle(contribution, context) }))
    .filter((tab): tab is typeof tab & { title: string } => tab.title !== null)
  if (visible.length === 0) {
    return null
  }
  const selected = visible.find((tab) => tab.contribution.id === selectedId) ?? visible[0]
  const tabId = (contributionId: string) => `${id}-tab-${contributionId}`
  const panelId = (contributionId: string) => `${id}-panel-${contributionId}`

  function select(index: number) {
    const tab = visible[(index + visible.length) % visible.length]
    setSelectedId(tab.contribution.id)
    tabs.current.get(tab.contribution.id)?.focus()
  }

  function handleKeyDown(event: KeyboardEvent) {
    const index = visible.indexOf(selected)
    const keys: Record<string, number> = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: visible.length - 1 }
    if (event.key in keys) {
      event.preventDefault()
      select(keys[event.key])
    }
  }

  return (
    <div className="analysis-tabs">
      <div className="tab-list" role="tablist" aria-labelledby={labelledBy} onKeyDown={handleKeyDown}>
        {visible.map(({ contribution, title }) => {
          const isSelected = contribution.id === selected.contribution.id
          return (
            <button
              key={contribution.id}
              ref={(element) => {
                if (element) {
                  tabs.current.set(contribution.id, element)
                } else {
                  tabs.current.delete(contribution.id)
                }
              }}
              type="button"
              role="tab"
              id={tabId(contribution.id)}
              className="tab"
              aria-selected={isSelected}
              aria-controls={panelId(contribution.id)}
              tabIndex={isSelected ? 0 : -1}
              onClick={() => setSelectedId(contribution.id)}
            >
              {title}
            </button>
          )
        })}
      </div>
      <div
        className="tab-panel"
        role="tabpanel"
        id={panelId(selected.contribution.id)}
        aria-labelledby={tabId(selected.contribution.id)}
        tabIndex={0}
        data-contribution={selected.contribution.id}
      >
        <SlotItem key={selected.contribution.id} slot="analysis.tabs" contribution={selected.contribution} context={context} />
      </div>
    </div>
  )
}
