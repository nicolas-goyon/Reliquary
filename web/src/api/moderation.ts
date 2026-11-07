/** The types of the moderation. See docs/architecture/decks.md and docs/decisions/0035-moderation-of-public-content.md. */

export const reportItemTypes = ['deck', 'author'] as const
export type ReportItemType = (typeof reportItemTypes)[number]

export const reportReasons = ['spam', 'offensive', 'copyright', 'other'] as const
export type ReportReason = (typeof reportReasons)[number]

export const reportStatuses = ['open', 'resolved', 'dismissed'] as const
export type ReportStatus = (typeof reportStatuses)[number]

export type ModerationAction = 'hide' | 'show' | 'resolve' | 'dismiss'

/** The maximum number of characters of a comment or of a reason. */
export const maxCommentLength = 500

export interface ModerationUser {
  id: string
  displayName: string
}

/** A report, as an administrator sees it. */
export interface ModerationReport {
  id: string
  itemType: ReportItemType
  itemId: string
  itemName: string
  owner: ModerationUser
  reporter: ModerationUser
  reason: ReportReason
  comment: string
  status: ReportStatus
  itemHidden: boolean
  createdAt: string
  closedAt: string | null
}

export interface HiddenItem {
  itemType: ReportItemType
  itemId: string
  itemName: string
  owner: ModerationUser
  reason: string
  hiddenAt: string
}

export interface ModerationLogEntry {
  id: string
  action: ModerationAction
  itemType: ReportItemType
  itemId: string | null
  itemName: string
  moderator: ModerationUser | null
  reportId: string | null
  reason: string
  createdAt: string
}

/** Gets the path of the page of an item. */
export function itemPath(itemType: ReportItemType, itemId: string): string {
  return itemType === 'deck' ? `/decks/${itemId}` : `/authors/${itemId}`
}
