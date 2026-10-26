import { Component, type ErrorInfo, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useSlotContributions, type SlotContexts, type SlotName } from './registry'

interface SlotProps<Name extends SlotName> {
  name: Name
  context: SlotContexts[Name]
  className?: string
}

/**
 * Renders the contributions of a slot. Each contribution has its own container and its own error boundary,
 * so a contribution that fails does not stop the page or the other contributions.
 */
export function Slot<Name extends SlotName>({ name, context, className }: SlotProps<Name>) {
  const contributions = useSlotContributions(name)
  return (
    <div className={className ? `slot ${className}` : 'slot'} data-slot={name}>
      {contributions.map((contribution) => (
        <div key={contribution.id} className="slot-contribution" data-contribution={contribution.id}>
          <ContributionBoundary slot={name} contributionId={contribution.id}>
            <Contribution render={() => contribution.render(context)} />
          </ContributionBoundary>
        </div>
      ))}
    </div>
  )
}

// The contribution renders inside the boundary, so the boundary catches its errors.
function Contribution({ render }: { render: () => ReactNode }) {
  return <>{render()}</>
}

interface BoundaryProps {
  slot: string
  contributionId: string
  children: ReactNode
}

class ContributionBoundary extends Component<BoundaryProps, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError(): { failed: boolean } {
    return { failed: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error(`The contribution "${this.props.contributionId}" of the slot "${this.props.slot}" failed.`, error, info.componentStack)
  }

  render() {
    return this.state.failed ? <ContributionFailed /> : this.props.children
  }
}

function ContributionFailed() {
  const { t } = useTranslation()
  return (
    <p className="contribution-failed" role="alert">
      {t('slots.contributionFailed')}
    </p>
  )
}
