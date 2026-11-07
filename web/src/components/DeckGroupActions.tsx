import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { folderTree, type DeckFolder, type GroupAction } from '../api/decks'
import { fileFormats, type FileFormat } from '../api/exchange'
import { useAuth } from '../auth/session'
import { ErrorAlert } from './Forms'

interface GroupActionsProps {
  deckIds: string[]
  folders: DeckFolder[]
  onDone(): void
}

/** The toolbar of the group actions on the selected decks. */
export function DeckGroupActions({ deckIds, folders, onDone }: GroupActionsProps) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const id = useId()
  const [folderId, setFolderId] = useState('')
  const [tag, setTag] = useState('')
  const [format, setFormat] = useState<FileFormat>('moxfield')
  const [error, setError] = useState<unknown>(null)

  async function apply(action: GroupAction, extra: Record<string, unknown> = {}) {
    if (action === 'delete' && !window.confirm(t('group.deleteConfirm', { count: deckIds.length }))) {
      return
    }
    setError(null)
    try {
      await request('/decks/group', { method: 'POST', body: { deckIds, action, ...extra } })
      onDone()
    } catch (caught) {
      setError(caught)
    }
  }

  async function exportDecks() {
    setError(null)
    try {
      const archive = await request<Blob>('/decks/export', { method: 'POST', body: { deckIds, format }, responseType: 'blob' })
      const url = URL.createObjectURL(archive)
      const link = document.createElement('a')
      link.href = url
      link.download = 'decks.zip'
      link.click()
      URL.revokeObjectURL(url)
    } catch (caught) {
      setError(caught)
    }
  }

  return (
    <div className="group-actions panel" role="toolbar" aria-label={t('group.title', { count: deckIds.length })}>
      <p className="group-count">{t('group.title', { count: deckIds.length })}</p>
      <div className="inline-field">
        <label htmlFor={`${id}-folder`}>{t('group.folder')}</label>
        <select id={`${id}-folder`} value={folderId} onChange={(event) => setFolderId(event.target.value)}>
          <option value="">{t('folders.none')}</option>
          {folderTree(folders).map(({ folder, depth }) => (
            <option key={folder.id} value={folder.id}>
              {`${'  '.repeat(depth)}${folder.name}`}
            </option>
          ))}
        </select>
        <button type="button" onClick={() => void apply('move', { folderId: folderId || null })}>
          {t('group.move')}
        </button>
      </div>
      <div className="inline-field">
        <label htmlFor={`${id}-tag`}>{t('group.tag')}</label>
        <input id={`${id}-tag`} maxLength={50} value={tag} onChange={(event) => setTag(event.target.value)} />
        <button type="button" disabled={tag.trim().length === 0} onClick={() => void apply('tag', { tag })}>
          {t('group.addTag')}
        </button>
        <button type="button" disabled={tag.trim().length === 0} onClick={() => void apply('untag', { tag })}>
          {t('group.removeTag')}
        </button>
      </div>
      <div className="inline-field">
        <button type="button" onClick={() => void apply('pin')}>
          {t('group.pin')}
        </button>
        <button type="button" onClick={() => void apply('unpin')}>
          {t('group.unpin')}
        </button>
        <button type="button" onClick={() => void apply('archive')}>
          {t('group.archive')}
        </button>
        <button type="button" onClick={() => void apply('unarchive')}>
          {t('group.unarchive')}
        </button>
      </div>
      <div className="inline-field">
        <label htmlFor={`${id}-format`}>{t('import.fileFormat')}</label>
        <select id={`${id}-format`} value={format} onChange={(event) => setFormat(event.target.value as FileFormat)}>
          {fileFormats.map((known) => (
            <option key={known} value={known}>
              {t(`fileFormats.${known}`)}
            </option>
          ))}
        </select>
        <button type="button" onClick={() => void exportDecks()}>
          {t('group.export')}
        </button>
      </div>
      <button type="button" className="button-danger" onClick={() => void apply('delete')}>
        {t('group.delete')}
      </button>
      <ErrorAlert error={error} />
    </div>
  )
}
