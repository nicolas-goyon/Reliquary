import { describe, expect, it } from 'vitest'
import { buildQuery, emptyFilters, filterTerms, parseQuery, splitTerms } from './searchFilters'

describe('splitTerms', () => {
  it('splits at the spaces and keeps the quotes, the regular expressions and the parentheses', () => {
    expect(splitTerms('  bolt  o:"deals 3"  name:/^light ning/ (t:goblin or t:elf) -c:r ')).toEqual([
      'bolt',
      'o:"deals 3"',
      'name:/^light ning/',
      '(t:goblin or t:elf)',
      '-c:r',
    ])
  })

  it('gives no term for an empty query', () => {
    expect(splitTerms('   ')).toEqual([])
  })
})

describe('parseQuery', () => {
  it('reads the terms that the panel writes', () => {
    const { filters, present, rest } = parseQuery('c=GW id:wubrg t:Creature t:land mv>=3 r:m f:modern s:MH3')
    expect(filters).toEqual({
      color: { mode: 'exactly', colors: ['w', 'g'] },
      identity: { mode: 'atMost', colors: ['w', 'u', 'b', 'r', 'g'] },
      types: ['creature', 'land'],
      manaValue: { operator: '>=', value: '3' },
      rarity: 'mythic',
      format: 'modern',
      set: 'mh3',
    })
    expect(present).toEqual({ color: true, identity: true, manaValue: true })
    expect(rest).toEqual([])
  })

  it('reads the other names of the keywords and the colon of each keyword', () => {
    const { filters } = parseQuery('color:r identity>=ub type:instant cmc:2 rarity=uncommon legal:commander set:m10')
    expect(filters.color).toEqual({ mode: 'including', colors: ['r'] })
    expect(filters.identity).toEqual({ mode: 'including', colors: ['u', 'b'] })
    expect(filters.types).toEqual(['instant'])
    expect(filters.manaValue).toEqual({ operator: '=', value: '2' })
    expect(filters.rarity).toBe('uncommon')
    expect(filters.format).toBe('commander')
    expect(filters.set).toBe('m10')
  })

  it('keeps the unknown, negated and repeated terms in the rest, with their text', () => {
    const { filters, rest } = parseQuery('Bolt -t:creature t:Goblin c:c mv>=2 mv<=4 r>=rare f:penny o:"draw a card" c:g c:r')
    expect(rest).toEqual(['Bolt', '-t:creature', 't:Goblin', 'c:c', 'mv<=4', 'r>=rare', 'f:penny', 'o:"draw a card"', 'c:r'])
    expect(filters.manaValue).toEqual({ operator: '>=', value: '2' })
    expect(filters.color.colors).toEqual(['g'])
  })

  it('reads no term when the query has an "or" outside parentheses', () => {
    const { filters, rest } = parseQuery('t:creature or t:artifact')
    expect(filters).toEqual(emptyFilters())
    expect(rest).toEqual(['t:creature', 'or', 't:artifact'])
  })
})

describe('filterTerms and buildQuery', () => {
  it('writes the supported keywords in a fixed order', () => {
    const filters = {
      color: { mode: 'atMost' as const, colors: ['g' as const, 'w' as const] },
      identity: { mode: 'exactly' as const, colors: ['u' as const] },
      types: ['land' as const, 'creature' as const],
      manaValue: { operator: '<' as const, value: '4' },
      rarity: 'rare' as const,
      format: 'pauper' as const,
      set: ' DMU ',
    }
    expect(filterTerms(filters)).toEqual(['c<=wg', 'id=u', 't:creature', 't:land', 'mv<4', 'r:rare', 'f:pauper', 's:dmu'])
  })

  it('writes the colon for "including" colors and for "at most" color identity', () => {
    const filters = { ...emptyFilters(), color: { mode: 'including' as const, colors: ['r' as const] }, identity: { mode: 'atMost' as const, colors: ['b' as const] } }
    expect(filterTerms(filters)).toEqual(['c:r', 'id:b'])
  })

  it('writes no term for the empty filters and for a mana value that is not a number', () => {
    expect(filterTerms({ ...emptyFilters(), manaValue: { operator: '=', value: 'x' } })).toEqual([])
    expect(buildQuery(['bolt'], emptyFilters())).toBe('bolt')
  })

  it('keeps the other terms before the filters', () => {
    const { filters, rest } = parseQuery('t:goblin c:r o:"draw a card"')
    expect(buildQuery(rest, { ...filters, format: 'legacy' })).toBe('t:goblin o:"draw a card" c:r f:legacy')
  })

  it('puts parentheses around other terms with an "or", so that the filters apply to all the results', () => {
    const { filters, rest } = parseQuery('t:goblin or t:elf')
    const query = buildQuery(rest, { ...filters, format: 'modern' })
    expect(query).toBe('(t:goblin or t:elf) f:modern')
    expect(parseQuery(query).filters.format).toBe('modern')
    expect(buildQuery(parseQuery(query).rest, emptyFilters())).toBe('(t:goblin or t:elf)')
  })
})
