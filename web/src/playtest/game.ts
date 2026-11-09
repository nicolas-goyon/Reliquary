import { applyAction, type TableAction, type TableState } from './engine'

/** An action of a game page: an action of the table engine, or a new game. */
export type GameAction = TableAction | { type: 'restart'; table: TableState }

/** The reducer of a game page. It gives all the table actions to the table engine. */
export function gameReducer(state: TableState, action: GameAction): TableState {
  return action.type === 'restart' ? action.table : applyAction(state, action)
}
