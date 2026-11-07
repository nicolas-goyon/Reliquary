import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

type Status =
  | { state: 'checking' }
  | { state: 'available'; version: string }
  | { state: 'unavailable' }

interface StatusResponse {
  version: string
}

/** Shows whether the API answers, and its version. */
export function ApiStatus() {
  const { t } = useTranslation()
  const [status, setStatus] = useState<Status>({ state: 'checking' })

  useEffect(() => {
    const controller = new AbortController()

    fetch('/api/v1/status', { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`The API answered with HTTP ${response.status}.`)
        }
        const body = (await response.json()) as StatusResponse
        setStatus({ state: 'available', version: body.version })
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setStatus({ state: 'unavailable' })
        }
      })

    return () => controller.abort()
  }, [])

  return (
    <p className="api-status" role="status" data-state={status.state}>
      {status.state === 'checking' && t('apiStatus.checking')}
      {status.state === 'available' && t('apiStatus.available', { version: status.version })}
      {status.state === 'unavailable' && t('apiStatus.unavailable')}
    </p>
  )
}
