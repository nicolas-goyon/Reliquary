import { useId, type CSSProperties } from 'react'
import { largestCount } from '../../api/analysis'

export interface Bar {
  key: string
  /** The name of the bar in the chart and in the text alternative. */
  label: string
  value: number
  /** An optional class of the bar, for example a mana color. */
  className?: string
}

interface BarChartProps {
  /** The title of the chart. The text alternative uses it as its caption. */
  title: string
  /** The header of the column of the labels in the text alternative. */
  labelHeader: string
  /** The header of the column of the values in the text alternative. */
  valueHeader: string
  bars: Bar[]
  /** Columns for an ordered scale, for example the mana curve. Rows for the other distributions. */
  orientation?: 'columns' | 'rows'
}

/**
 * A simple bar chart in HTML and CSS. A screen reader reads the visually hidden table, and the bars are hidden from it.
 */
export function BarChart({ title, labelHeader, valueHeader, bars, orientation = 'rows' }: BarChartProps) {
  const id = useId()
  const largest = largestCount(bars.map((bar) => bar.value))
  return (
    <figure className="bar-chart" aria-labelledby={`${id}-title`}>
      <figcaption id={`${id}-title`} className="bar-chart-title">
        {title}
      </figcaption>
      <table className="visually-hidden">
        <caption>{title}</caption>
        <thead>
          <tr>
            <th scope="col">{labelHeader}</th>
            <th scope="col">{valueHeader}</th>
          </tr>
        </thead>
        <tbody>
          {bars.map((bar) => (
            <tr key={bar.key}>
              <th scope="row">{bar.label}</th>
              <td>{bar.value}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className={`bar-chart-bars bar-chart-${orientation}`} aria-hidden="true">
        {bars.map((bar) => (
          <div key={bar.key} className="bar-chart-item">
            <span className="bar-chart-label">{bar.label}</span>
            <span className="bar-chart-track">
              <span className={bar.className ? `bar-chart-bar ${bar.className}` : 'bar-chart-bar'} style={{ '--bar-size': `${(bar.value / largest) * 100}%` } as CSSProperties} />
            </span>
            <span className="bar-chart-value">{bar.value}</span>
          </div>
        ))}
      </div>
    </figure>
  )
}
