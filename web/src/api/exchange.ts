/** The file formats of the deck import and the deck export. See docs/development/deck-import-export.md. */
export const fileFormats = ['text', 'moxfield', 'mtga', 'mtgo', 'dek', 'archidekt', 'csv', 'deckbox', 'manabox', 'dragonshield'] as const
export type FileFormat = (typeof fileFormats)[number]

/** The fields of a deck line that a column of a generic CSV file can give. */
export const csvFields = ['quantity', 'name', 'setCode', 'collectorNumber', 'finish', 'zone', 'tags', 'globalTags', 'categories', 'note'] as const
export type CsvField = (typeof csvFields)[number]

/** The header names that the API finds without a column map, for each field of the generic CSV format. */
const knownHeaders: Record<CsvField, string[]> = {
  quantity: ['quantity', 'count', 'qty', 'amount'],
  name: ['name', 'cardname', 'card'],
  setCode: ['setcode', 'set', 'editioncode'],
  collectorNumber: ['collectornumber', 'cardnumber', 'number'],
  finish: ['finish', 'foil', 'printing'],
  zone: ['zone', 'board', 'section'],
  tags: ['tags', 'tag'],
  globalTags: ['globaltags'],
  categories: ['categories'],
  note: ['note', 'notes'],
}

export interface ImportWarning {
  line: number
  code: string
  text: string
}

export interface ExportedFile {
  fileName: string
  contentType: string
  content: string
}

function normalize(header: string): string {
  return header.replace(/[\s_-]/g, '').toLowerCase()
}

/**
 * Reads the header names of a CSV file. The function ignores a first line "sep=," and finds the separator like the API.
 */
export function csvHeaders(content: string): string[] {
  const lines = content.replace(/^﻿/, '').split('\n')
  let index = 0
  let separator = ','
  const first = lines[0]?.trim().replace(/^"|"$/g, '') ?? ''
  if (/^sep=.$/i.test(first)) {
    separator = first[4]
    index = 1
  } else {
    separator = [',', ';', '\t'].reduce((best, candidate) => (first.split(candidate).length > first.split(best).length ? candidate : best), ',')
  }
  const header = (lines[index] ?? '').replace(/\r$/, '')
  return header.split(separator).map((cell) => cell.trim().replace(/^"(.*)"$/, '$1').replace(/""/g, '"')).filter((cell) => cell.length > 0)
}

/** Gets the header that the API uses for a field when the request has no column map, or undefined. */
export function automaticHeader(field: CsvField, headers: string[]): string | undefined {
  for (const candidate of knownHeaders[field]) {
    const found = headers.find((header) => normalize(header) === candidate)
    if (found) {
      return found
    }
  }
  return undefined
}

/** Finds the probable format of a file from its name and its content. */
export function detectFormat(fileName: string, content: string): FileFormat {
  const lowerName = fileName.toLowerCase()
  if (lowerName.endsWith('.dek') || content.trimStart().startsWith('<')) {
    return 'dek'
  }
  if (lowerName.endsWith('.csv')) {
    const headers = csvHeaders(content).map(normalize)
    if (headers.includes('manaboxid')) {
      return 'manabox'
    }
    if (headers.includes('foldername') && headers.includes('cardname')) {
      return 'dragonshield'
    }
    if (headers.includes('tradelistcount') || headers.includes('editioncode')) {
      return 'deckbox'
    }
    return 'csv'
  }
  if (/\[[^\]]*\]\s*$/m.test(content) && /^\d+x /m.test(content)) {
    return 'archidekt'
  }
  if (/^(Deck|About)\s*$/m.test(content)) {
    return 'mtga'
  }
  return 'moxfield'
}

/** Gets the name of a deck from the name of its file, without the extension. */
export function deckNameFromFile(fileName: string): string {
  return fileName.replace(/\.[^.]+$/, '').trim()
}
