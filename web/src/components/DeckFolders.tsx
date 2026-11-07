import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { folderTree, type DeckFolder } from '../api/decks'
import { useAuth } from '../auth/session'
import { ErrorAlert } from './Forms'

interface FolderTreeProps {
  folders: DeckFolder[]
  /** The selected folder: a folder ID, `none`, or empty for all the decks. */
  selected: string
  onSelect(folderId: string): void
  onChanged(): void
}

/** The folder tree of the deck list, with the creation, the rename and the deletion of the folders. */
export function DeckFolderTree({ folders, selected, onSelect, onChanged }: FolderTreeProps) {
  const { t } = useTranslation('decks')
  const { request } = useAuth()
  const [newName, setNewName] = useState('')
  const [renaming, setRenaming] = useState<string | null>(null)
  const [renameValue, setRenameValue] = useState('')
  const [error, setError] = useState<unknown>(null)

  async function send(action: () => Promise<unknown>) {
    setError(null)
    try {
      await action()
      onChanged()
    } catch (caught) {
      setError(caught)
    }
  }

  function create(event: FormEvent) {
    event.preventDefault()
    const parentId = selected && selected !== 'none' ? selected : null
    void send(async () => {
      await request('/deck-folders', { method: 'POST', body: { name: newName, parentId } })
      setNewName('')
    })
  }

  function rename(event: FormEvent, folderId: string) {
    event.preventDefault()
    void send(async () => {
      await request(`/deck-folders/${folderId}`, { method: 'PATCH', body: { name: renameValue } })
      setRenaming(null)
    })
  }

  function remove(folder: DeckFolder) {
    if (!window.confirm(t('folders.deleteConfirm', { name: folder.name }))) {
      return
    }
    void send(async () => {
      await request(`/deck-folders/${folder.id}`, { method: 'DELETE' })
      if (selected === folder.id) {
        onSelect('')
      }
    })
  }

  const entry = (value: string, label: string, depth = 0, count?: number) => (
    <button type="button" className="folder-entry" style={{ paddingInlineStart: `${0.5 + depth}rem` }} aria-current={selected === value ? 'true' : undefined} onClick={() => onSelect(value)}>
      <span>{label}</span>
      {count !== undefined && <span className="folder-count">{count}</span>}
    </button>
  )

  return (
    <nav className="deck-folders" aria-label={t('folders.title')}>
      <h2>{t('folders.title')}</h2>
      <ul>
        <li>{entry('', t('folders.all'))}</li>
        <li>{entry('none', t('folders.none'))}</li>
        {folderTree(folders).map(({ folder, depth }) => (
          <li key={folder.id}>
            {renaming === folder.id ? (
              <form className="folder-rename" onSubmit={(event) => rename(event, folder.id)}>
                <label className="visually-hidden" htmlFor={`rename-${folder.id}`}>
                  {t('folders.renameLabel', { name: folder.name })}
                </label>
                <input id={`rename-${folder.id}`} maxLength={50} autoFocus value={renameValue} onChange={(event) => setRenameValue(event.target.value)} />
                <button type="submit">{t('actions.save')}</button>
                <button type="button" onClick={() => setRenaming(null)}>
                  {t('actions.cancel')}
                </button>
              </form>
            ) : (
              <div className="folder-row">
                {entry(folder.id, folder.name, depth, folder.deckCount)}
                <button
                  type="button"
                  className="button-quiet"
                  aria-label={t('folders.rename', { name: folder.name })}
                  onClick={() => {
                    setRenaming(folder.id)
                    setRenameValue(folder.name)
                  }}
                >
                  ✎
                </button>
                <button type="button" className="button-quiet" aria-label={t('folders.delete', { name: folder.name })} onClick={() => remove(folder)}>
                  ×
                </button>
              </div>
            )}
          </li>
        ))}
      </ul>
      <form className="folder-create" onSubmit={create}>
        <label htmlFor="new-folder">{selected && selected !== 'none' ? t('folders.createIn') : t('folders.create')}</label>
        <div className="inline-field">
          <input id="new-folder" maxLength={50} value={newName} onChange={(event) => setNewName(event.target.value)} />
          <button type="submit" disabled={newName.trim().length === 0}>
            {t('folders.createSubmit')}
          </button>
        </div>
      </form>
      <ErrorAlert error={error} />
    </nav>
  )
}
