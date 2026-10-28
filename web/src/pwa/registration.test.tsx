import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { UpdatePrompt } from '../components/UpdatePrompt'
import i18n from '../i18n'
import { registerServiceWorker, resetRegistrationState } from './registration'

/** A fake service worker container with one registration. */
function fakeContainer(options: { controller: boolean; waiting?: boolean }) {
  const containerEvents = new EventTarget()
  const registrationEvents = new EventTarget()
  const waiting = options.waiting ? { postMessage: vi.fn<(message: unknown) => void>() } : null
  const registration = Object.assign(registrationEvents, {
    waiting,
    installing: null as (EventTarget & { state: string; postMessage(message: unknown): void }) | null,
    update: vi.fn<() => Promise<undefined>>(async () => undefined),
  })
  const container = Object.assign(containerEvents, {
    controller: options.controller ? {} : null,
    register: vi.fn<(url: string, options: RegistrationOptions) => Promise<typeof registration>>(async () => registration),
  })
  return { container: container as unknown as ServiceWorkerContainer, registration, waiting, events: containerEvents }
}

describe('service worker registration', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en')
    resetRegistrationState()
  })

  afterEach(() => {
    resetRegistrationState()
  })

  it('registers /sw.js for the full site', async () => {
    const { container } = fakeContainer({ controller: false })
    await registerServiceWorker(container, vi.fn<() => void>())

    expect(container.register).toHaveBeenCalledWith('/sw.js', { scope: '/' })
  })

  it('shows no prompt for the first installation', async () => {
    const { container, registration } = fakeContainer({ controller: false })
    render(<UpdatePrompt />)
    await registerServiceWorker(container, vi.fn<() => void>())

    const worker = Object.assign(new EventTarget(), { state: 'installed', postMessage: vi.fn<(message: unknown) => void>() })
    registration.installing = worker
    act(() => {
      registration.dispatchEvent(new Event('updatefound'))
      worker.dispatchEvent(new Event('statechange'))
    })

    expect(screen.queryByRole('button', { name: 'Update' })).not.toBeInTheDocument()
  })

  it('shows a prompt for a new version, and loads the page again after the user accepts', async () => {
    const reload = vi.fn<() => void>()
    const { container, registration, events } = fakeContainer({ controller: true })
    render(<UpdatePrompt />)
    await registerServiceWorker(container, reload)

    const worker = Object.assign(new EventTarget(), { state: 'installing', postMessage: vi.fn<(message: unknown) => void>() })
    registration.installing = worker
    act(() => {
      registration.dispatchEvent(new Event('updatefound'))
      worker.state = 'installed'
      worker.dispatchEvent(new Event('statechange'))
    })

    expect(screen.getByRole('status')).toHaveTextContent('A new version of Reliquary is available.')
    await userEvent.setup().click(screen.getByRole('button', { name: 'Update' }))
    expect(worker.postMessage).toHaveBeenCalledWith({ type: 'skipWaiting' })

    events.dispatchEvent(new Event('controllerchange'))
    expect(reload).toHaveBeenCalledOnce()
  })

  it('shows the prompt for a worker that already waits', async () => {
    const { container } = fakeContainer({ controller: true, waiting: true })
    render(<UpdatePrompt />)
    await act(() => registerServiceWorker(container, vi.fn<() => void>()))

    expect(screen.getByRole('button', { name: 'Update' })).toBeInTheDocument()
  })

  it('does not load the page again when the user did not ask for the update', async () => {
    const reload = vi.fn<() => void>()
    const { container, events } = fakeContainer({ controller: false })
    await registerServiceWorker(container, reload)

    events.dispatchEvent(new Event('controllerchange'))
    expect(reload).not.toHaveBeenCalled()
  })
})
