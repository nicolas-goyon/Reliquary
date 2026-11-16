import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatKey, type Deck } from '../api/decks'
import { Dialog } from '../components/versions/Dialog'
import { makeDeckImage } from './deckImage'
import { deckImageStyles, imageFileName, isZoneGroup, type DeckImageStyle, type ImageGroup } from './deckImageLayout'
import { embedSnippet } from './embed'

type Status = { state: 'idle' } | { state: 'working' } | { state: 'done'; failed: string[] } | { state: 'error' }

/** Gives a file to the user as a download. */
function download(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  // Some browsers start the download after the click event. The delay keeps the address valid until then.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/**
 * The dialog that shares a public deck: a PNG image for Discord and the forums, and the HTML code of an embed for other sites.
 * See docs/decisions/0046-deck-image-and-embed.md.
 */
export function ShareDialog({ deck, author, onClose }: { deck: Deck; author: string; onClose(): void }) {
  const { t } = useTranslation('decks')
  const id = useId()
  const [style, setStyle] = useState<DeckImageStyle>('list')
  const [status, setStatus] = useState<Status>({ state: 'idle' })
  const [copied, setCopied] = useState(false)
  const snippet = embedSnippet(window.location.origin, deck.id, deck.name)
  const total = deck.counts.commander + deck.counts.companion + deck.counts.main

  async function makeImage() {
    setStatus({ state: 'working' })
    try {
      const image = await makeDeckImage(deck, style, {
        subtitle: [t(`formats.${formatKey(deck.format)}`), t('editor.cardCount', { count: total }), author ? t('share.byAuthor', { name: author }) : '']
          .filter(Boolean)
          .join(' · '),
        groupTitle: (group: ImageGroup) => t('share.groupTitle', { name: groupName(group), count: group.count }),
        footer: window.location.host,
      })
      download(image.blob, imageFileName(deck.name))
      setStatus({ state: 'done', failed: image.failed })
    } catch {
      setStatus({ state: 'error' })
    }
  }

  /** Gets the name of a group: a zone name, or a card type for the groups of the main deck. */
  function groupName(group: ImageGroup): string {
    return isZoneGroup(group.key) ? t(`zones.${group.key}`) : t(`analysis.types.keys.${group.key}`)
  }

  async function copySnippet() {
    try {
      await navigator.clipboard.writeText(snippet)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  return (
    <Dialog title={t('share.title')} onClose={onClose}>
      <section aria-labelledby={`${id}-image`}>
        <h3 id={`${id}-image`}>{t('share.imageTitle')}</h3>
        <p className="field-hint">{t('share.imageHint')}</p>
        <fieldset className="field">
          <legend>{t('share.style')}</legend>
          {deckImageStyles.map((value) => (
            <label key={value} className="checkbox">
              <input type="radio" name={`${id}-style`} value={value} checked={style === value} onChange={() => setStyle(value)} />
              {t(`share.styles.${value}`)}
            </label>
          ))}
        </fieldset>
        <button type="button" className="button-primary" disabled={status.state === 'working'} onClick={() => void makeImage()}>
          {t('share.download')}
        </button>
        <div role="status" className="field-hint">
          {status.state === 'working' && t('share.working')}
          {status.state === 'done' && (status.failed.length > 0 ? t('share.failed', { count: status.failed.length }) : t('share.done'))}
        </div>
        {status.state === 'error' && (
          <p role="alert" className="alert alert-error">
            {t('share.error')}
          </p>
        )}
      </section>
      <section aria-labelledby={`${id}-embed`}>
        <h3 id={`${id}-embed`}>{t('share.embedTitle')}</h3>
        <p className="field-hint">{t('share.embedHint')}</p>
        <label className="visually-hidden" htmlFor={`${id}-code`}>
          {t('share.embedCode')}
        </label>
        <textarea id={`${id}-code`} className="deck-text" rows={4} readOnly value={snippet} onFocus={(event) => event.currentTarget.select()} />
        <button type="button" onClick={() => void copySnippet()}>
          {t('share.copyCode')}
        </button>
        {copied && (
          <span role="status" className="field-hint">
            {' '}
            {t('share.codeCopied')}
          </span>
        )}
      </section>
      <div className="form-actions">
        <button type="button" onClick={onClose}>
          {t('share.close')}
        </button>
      </div>
    </Dialog>
  )
}
