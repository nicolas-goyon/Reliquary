import { useTranslation } from 'react-i18next'

const colorClass: Record<string, string> = { W: 'white', U: 'blue', B: 'black', R: 'red', G: 'green', C: 'colorless' }

/**
 * Shows a mana cost, for example {2}{U}{U}, with one round symbol for each mana symbol.
 * A screen reader reads the cost as text.
 */
export function ManaCost({ cost }: { cost: string | null | undefined }) {
  const { t } = useTranslation('cards')
  if (!cost) {
    return null
  }

  const symbols = [...cost.matchAll(/\{([^}]+)\}|\s*\/\/\s*/g)].map((match) => match[1] ?? '//')
  return (
    <span className="mana-cost" role="img" aria-label={t('manaCost', { cost })}>
      {symbols.map((symbol, index) =>
        symbol === '//' ? (
          <span key={index} className="mana-separator" aria-hidden="true">
            //
          </span>
        ) : (
          <span key={index} className={`mana-symbol mana-${colorClass[symbol] ?? (symbol.includes('/') ? 'hybrid' : 'generic')}`} aria-hidden="true">
            {symbol}
          </span>
        ),
      )}
    </span>
  )
}
