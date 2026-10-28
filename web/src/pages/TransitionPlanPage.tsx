import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { locationLabel } from '../api/collection'
import type { Deck } from '../api/decks'
import { notAssembledCode, transitionSteps, type TransitionCopy, type TransitionPlan, type TransitionStep } from '../api/versions'
import { useAuth } from '../auth/session'
import { ErrorAlert } from '../components/Forms'
import { Link } from '../router/Link'
import { navigate } from '../router/navigation'
import '../components/versions/versions.css'

type Loaded = { state: 'loading' } | { state: 'error'; error: unknown } | { state: 'done'; deck: Deck; plan: TransitionPlan }

function storageKey(plan: TransitionPlan): string {
  return `reliquary.transition.${plan.deckId}.${plan.assembledVersionId}`
}

function readDone(plan: TransitionPlan): string[] {
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(storageKey(plan)) ?? '[]')
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
  } catch {
    return []
  }
}

function writeDone(plan: TransitionPlan, keys: string[]) {
  try {
    window.localStorage.setItem(storageKey(plan), JSON.stringify(keys))
  } catch {
    // The browser can refuse the storage. The check boxes then stay for this page only.
  }
}

/**
 * The transition plan: the physical steps from the assembled version to the working list of a variant or of the main deck.
 * Each step has a check box. The browser keeps the checked steps.
 */
export function TransitionPlanPage({ id }: { id: string }) {
  const { t } = useTranslation('versions')
  const { state, request } = useAuth()
  const [loaded, setLoaded] = useState<Loaded>({ state: 'loading' })
  const [done, setDone] = useState<string[]>([])

  useEffect(() => {
    if (state.status === 'anonymous') {
      navigate(`/sign-in?returnUrl=${encodeURIComponent(`/decks/${id}/transition-plan`)}`, { replace: true })
      return
    }
    if (state.status !== 'authenticated') {
      return
    }
    Promise.all([request<Deck>(`/decks/${id}`), request<TransitionPlan>(`/decks/${id}/transition-plan`)])
      .then(([deck, plan]) => {
        setDone(readDone(plan))
        setLoaded({ state: 'done', deck, plan })
      })
      .catch((error: unknown) => setLoaded({ state: 'error', error }))
  }, [id, request, state.status])

  useEffect(() => {
    if (loaded.state === 'done') {
      document.title = t('transition.pageTitle', { name: loaded.deck.name })
    }
  }, [loaded, t])

  if (state.status !== 'authenticated') {
    return null
  }

  function toggle(plan: TransitionPlan, key: string) {
    const next = done.includes(key) ? done.filter((item) => item !== key) : [...done, key]
    setDone(next)
    writeDone(plan, next)
  }

  return (
    <section className="page page-wide transition-plan">
      <div className="page-header">
        <h1>{loaded.state === 'done' ? t('transition.title', { name: loaded.deck.name }) : t('transition.titleShort')}</h1>
        <div className="form-actions">
          <Link to={`/decks/${id}`}>{t('transition.backToDeck')}</Link>
        </div>
      </div>
      <p className="field-hint">{t('transition.help')}</p>
      {loaded.state === 'loading' && <p role="status">{t('transition.loading')}</p>}
      {loaded.state === 'error' &&
        (loaded.error instanceof ApiError && loaded.error.code === notAssembledCode ? (
          <p className="alert alert-info">{t('transition.notAssembled')}</p>
        ) : (
          <ErrorAlert error={loaded.error} />
        ))}
      {loaded.state === 'done' && <PlanView plan={loaded.plan} done={done} onToggle={(key) => toggle(loaded.plan, key)} />}
    </section>
  )
}

function PlanView({ plan, done, onToggle }: { plan: TransitionPlan; done: string[]; onToggle(key: string): void }) {
  const { t } = useTranslation('versions')
  const steps = transitionSteps(plan)
  if (steps.length === 0) {
    return <p className="alert alert-success">{t('transition.nothing')}</p>
  }

  const groups: { kind: TransitionStep['kind']; title: string }[] = [
    { kind: 'remove', title: t('transition.remove') },
    { kind: 'take', title: t('transition.take') },
    { kind: 'buy', title: t('transition.buy') },
  ]
  const finished = steps.filter((step) => done.includes(step.key)).length
  return (
    <>
      <p role="status">{t('transition.progress', { done: finished, count: steps.length })}</p>
      {plan.unlockFirst.length > 0 && (
        <div className="alert alert-warning">
          <p>{t('transition.unlockFirst')}</p>
          <ul>
            {plan.unlockFirst.map((location) => (
              <li key={location.locationId}>
                <Link to={`/collection/locations/${location.locationId}`}>{locationLabel(location.locationName, location.sectionName)}</Link>
              </li>
            ))}
          </ul>
        </div>
      )}
      {groups.map((group) => {
        const groupSteps = steps.filter((step) => step.kind === group.kind)
        const count = groupSteps.reduce((total, step) => total + step.quantity, 0)
        return (
          <section key={group.kind} className="panel" aria-labelledby={`transition-${group.kind}`}>
            <h2 id={`transition-${group.kind}`}>
              {group.title} <span className="zone-count">({count})</span>
            </h2>
            {groupSteps.length === 0 ? (
              <p className="field-hint">{t('transition.none')}</p>
            ) : (
              <ul className="transition-steps">
                {groupSteps.map((step) => (
                  <li key={step.key}>
                    <label>
                      <input type="checkbox" checked={done.includes(step.key)} onChange={() => onToggle(step.key)} /> <StepText plan={plan} step={step} />
                    </label>
                    {step.kind === 'buy' && step.closest.length > 0 && <Closest copies={step.closest} />}
                  </li>
                ))}
              </ul>
            )}
          </section>
        )
      })}
    </>
  )
}

function StepText({ plan, step }: { plan: TransitionPlan; step: TransitionStep }) {
  const { t } = useTranslation('versions')
  const name = plan.cards[step.oracleId]?.name ?? step.oracleId
  if (step.kind === 'take') {
    return (
      <>
        {t('transition.takeStep', { count: step.quantity, name, location: locationLabel(step.copy.locationName, step.copy.sectionName) })}
        {step.copy.language !== 'en' && <span className="badge">{step.copy.language.toUpperCase()}</span>}
        {step.copy.locked && <span className="badge">{t('transition.locked')}</span>}
      </>
    )
  }
  return <>{t(step.kind === 'remove' ? 'transition.removeStep' : 'transition.buyStep', { count: step.quantity, name })}</>
}

const reasonKeys = ['printing', 'language', 'finish', 'condition', 'unavailable'] as const

function Closest({ copies }: { copies: TransitionCopy[] }) {
  const { t } = useTranslation('versions')
  return (
    <ul className="transition-closest">
      {copies.map((copy, index) => (
        <li key={`${copy.locationId}-${index}`}>
          {t('transition.closest', {
            count: copy.quantity,
            language: copy.language.toUpperCase(),
            location: locationLabel(copy.locationName, copy.sectionName),
            reasons: copy.reasons
              .filter((reason): reason is (typeof reasonKeys)[number] => (reasonKeys as readonly string[]).includes(reason))
              .map((reason) => t(`transition.reasons.${reason}`))
              .join(', '),
          })}
        </li>
      ))}
    </ul>
  )
}
