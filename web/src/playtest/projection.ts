import { tableZones, type TableCard, type TablePlayer, type TableState, type TableZone } from './engine'

/*
 * The public projection of a table: the state that one viewer can see. ADR 0045 gives the design.
 * The shared screen uses it for the table window and the player windows. Online games can use the same function to send the state to each player.
 */

/** The viewer ID of a viewer that is not a player, for example the table window. This viewer sees only public information. */
export const publicViewerId = 'table'

/** Gives a card that has no identity: no Oracle ID, no name and no image. Only the position and the public marks stay. */
function maskedCard(card: TableCard, id: string): TableCard {
  return {
    id,
    oracleId: '',
    name: '',
    imageUri: null,
    isLand: false,
    manaValue: 0,
    isToken: false,
    tapped: card.tapped,
    faceDown: true,
    counters: card.counters,
  }
}

/**
 * Tells if a viewer can see the identity of a card in a zone of a player.
 * - Nobody sees the cards of a library. The owner also does not know the order of the library.
 * - Only the owner sees the cards of the hand and the face-down cards.
 * - All the other cards are public.
 */
function isVisible(card: TableCard, zone: TableZone, isOwner: boolean): boolean {
  if (zone === 'library') {
    return false
  }
  if (zone === 'hand' || card.faceDown) {
    return isOwner
  }
  return true
}

function projectPlayer(player: TablePlayer, viewerId: string): TablePlayer {
  const isOwner = player.id === viewerId
  const zones = {} as Record<TableZone, TableCard[]>
  for (const zone of tableZones) {
    // The ID of a masked card tells only its player, its zone and its position. The real ID contains the deck line.
    zones[zone] = player.zones[zone].map((card, index) => (isVisible(card, zone, isOwner) ? card : maskedCard(card, `hidden-${player.id}-${zone}-${index}`)))
  }
  return { ...player, zones }
}

/**
 * Gives the state that a viewer can see. It is a pure function.
 * - The result keeps the number of cards of each zone, the life points and all the counters.
 * - The result has no card identity of a hidden card: hands of other players, libraries and face-down cards of other players.
 * - The result has no seed, because the seed gives the order of each library after a shuffle.
 * @param viewerId The ID of a player, or publicViewerId for a viewer that is not a player.
 */
export function projectPublicState(state: TableState, viewerId: string): TableState {
  return { ...state, seed: 0, players: state.players.map((player) => projectPlayer(player, viewerId)) }
}
