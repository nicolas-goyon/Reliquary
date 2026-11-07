import { useTranslation } from 'react-i18next'
import { sortByZoneAndName, splitQuantities, signed, zoneOf, type ListChanges, type PrintingCopies, type QuantityChange } from '../../api/versions'
import { Link } from '../../router/Link'
import './versions.css'

interface ListChangesViewProps {
  changes: ListChanges
  /** The name of the first list in the column headers, for example "Parent version". */
  beforeLabel: string
  /** The name of the second list in the column headers. */
  afterLabel: string
}

/**
 * The differences between two lists: the added cards, the removed cards, the note changes,
 * and the printing changes in a panel that is closed by default.
 */
export function ListChangesView({ changes, beforeLabel, afterLabel }: ListChangesViewProps) {
  const { t } = useTranslation('versions')
  const { added, removed } = splitQuantities(sortByZoneAndName(changes.quantities, changes.cards))
  const notes = sortByZoneAndName(changes.notes, changes.cards)
  const printings = sortByZoneAndName(changes.printings, changes.cards)

  return (
    <div className="list-changes">
      {added.length === 0 && removed.length === 0 && <p className="field-hint">{t('changes.noQuantityChanges')}</p>}
      {added.length > 0 && <QuantityTable title={t('changes.added', { count: sum(added) })} changes={added} cards={changes.cards} beforeLabel={beforeLabel} afterLabel={afterLabel} />}
      {removed.length > 0 && <QuantityTable title={t('changes.removed', { count: sum(removed) })} changes={removed} cards={changes.cards} beforeLabel={beforeLabel} afterLabel={afterLabel} />}
      {notes.length > 0 && (
        <table className="version-table">
          <caption>{t('changes.notes', { count: notes.length })}</caption>
          <thead>
            <tr>
              <th scope="col">{t('changes.card')}</th>
              <th scope="col">{t('changes.zone')}</th>
              <th scope="col">{beforeLabel}</th>
              <th scope="col">{afterLabel}</th>
            </tr>
          </thead>
          <tbody>
            {notes.map((change) => (
              <tr key={`${change.oracleId}-${change.zone}`}>
                <th scope="row">
                  <CardLink oracleId={change.oracleId} name={changes.cards[change.oracleId]?.name} />
                </th>
                <td>
                  <ZoneName zone={change.zone} />
                </td>
                <td>{change.before || <span className="field-hint">{t('changes.noNote')}</span>}</td>
                <td>{change.after || <span className="field-hint">{t('changes.noNote')}</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <details className="version-details">
        <summary>{t('changes.printings', { count: printings.length })}</summary>
        {printings.length === 0 ? (
          <p className="field-hint">{t('changes.noPrintingChanges')}</p>
        ) : (
          <table className="version-table">
            <caption className="visually-hidden">{t('changes.printingsCaption')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('changes.card')}</th>
                <th scope="col">{t('changes.zone')}</th>
                <th scope="col">{beforeLabel}</th>
                <th scope="col">{afterLabel}</th>
              </tr>
            </thead>
            <tbody>
              {printings.map((change) => (
                <tr key={`${change.oracleId}-${change.zone}`}>
                  <th scope="row">
                    <CardLink oracleId={change.oracleId} name={changes.cards[change.oracleId]?.name} />
                  </th>
                  <td>
                    <ZoneName zone={change.zone} />
                  </td>
                  <td>
                    <PrintingList copies={change.before} changes={changes} />
                  </td>
                  <td>
                    <PrintingList copies={change.after} changes={changes} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </details>
    </div>
  )
}

function sum(changes: QuantityChange[]): number {
  return changes.reduce((total, change) => total + Math.abs(change.after - change.before), 0)
}

interface QuantityTableProps {
  title: string
  changes: QuantityChange[]
  cards: ListChanges['cards']
  beforeLabel: string
  afterLabel: string
}

function QuantityTable({ title, changes, cards, beforeLabel, afterLabel }: QuantityTableProps) {
  const { t, i18n } = useTranslation('versions')
  const language = i18n.resolvedLanguage ?? 'en'
  return (
    <table className="version-table">
      <caption>{title}</caption>
      <thead>
        <tr>
          <th scope="col">{t('changes.card')}</th>
          <th scope="col">{t('changes.zone')}</th>
          <th scope="col" className="version-number">
            {beforeLabel}
          </th>
          <th scope="col" className="version-number">
            {afterLabel}
          </th>
          <th scope="col" className="version-number">
            {t('changes.difference')}
          </th>
        </tr>
      </thead>
      <tbody>
        {changes.map((change) => (
          <tr key={`${change.oracleId}-${change.zone}`}>
            <th scope="row">
              <CardLink oracleId={change.oracleId} name={cards[change.oracleId]?.name} />
            </th>
            <td>
              <ZoneName zone={change.zone} />
            </td>
            <td className="version-number">{change.before}</td>
            <td className="version-number">{change.after}</td>
            <td className={change.after > change.before ? 'version-number version-up' : 'version-number version-down'}>{signed(change.after - change.before, language)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function CardLink({ oracleId, name }: { oracleId: string; name: string | undefined }) {
  return <Link to={`/cards/${oracleId}`}>{name ?? oracleId}</Link>
}

function ZoneName({ zone }: { zone: string }) {
  const { t } = useTranslation('decks')
  return <>{t(`zones.${zoneOf(zone)}`)}</>
}

/** The printings of a card, for example "MH2 #301, Foil, ×2". */
function PrintingList({ copies, changes }: { copies: PrintingCopies[]; changes: ListChanges }) {
  const { t } = useTranslation('versions')
  const { t: td } = useTranslation('decks')
  return (
    <ul className="version-printings">
      {copies.map((copy, index) => {
        const printing = copy.printingId ? changes.printingData[copy.printingId] : undefined
        const name = printing ? t('changes.printing', { set: printing.setCode.toUpperCase(), number: printing.collectorNumber }) : t('changes.defaultPrinting')
        const finish = copy.finish && copy.finish !== 'nonfoil' ? td(`finishes.${copy.finish === 'etched' ? 'etched' : 'foil'}`) : null
        return <li key={`${copy.printingId ?? 'default'}-${copy.finish ?? ''}-${index}`}>{t('changes.copies', { count: copy.quantity, printing: finish ? `${name}, ${finish}` : name })}</li>
      })}
    </ul>
  )
}
