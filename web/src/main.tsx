import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './i18n'
import './styles.css'
import { App } from './App'
import { registerServiceWorker } from './pwa/registration'
import { applyTheme, readStoredTheme } from './theme/theme'

const root = document.getElementById('root')
if (!root) {
  throw new Error('The page has no element with the ID "root".')
}

applyTheme(readStoredTheme())

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

// The development server has no service worker. Only the build has the file /sw.js.
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    registerServiceWorker(navigator.serviceWorker).catch((error: unknown) => console.warn('The service worker registration failed.', error))
  })
}
