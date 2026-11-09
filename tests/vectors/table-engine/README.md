# Conformance vectors of the table engine

The web tests in `web/src/playtest/engineVectors.test.ts` write these files from `web/src/playtest/engine.ts`.
The C# tests in `tests/Reliquary.Playtest.Tests/TableEngineConformanceTests.cs` replay them with `TableEngine`, so that the two engines stay equal.
Issue [#227](https://github.com/nicolas-goyon/reliquary/issues/227) and [ADR 0047](../../../docs/decisions/0047-trusted-server-for-online-games.md) give the design.

Each file is one game with a seed:

| Item | Content |
| --- | --- |
| `seed`, `startingLife`, `commander`, `setups` | The arguments of `createTable` |
| `initial` | The state that `createTable` gives |
| `steps` | One line for each action: `action`, and `changes` from the state of the step before |
| `final` | The state after the last action |

A change is `[path, value]`: the path of a value in the JSON of the state, and its new value.
The C# test applies the changes to get the expected state after each action.
The JSON of the C# state must be equal to the expected state, character for character.

The files have random games with 2, 3 and 4 players, with and without commander damage, and scripted games for the edge cases.
Each action type of `TableAction` changes the state in at least one step.
The total size of the files must stay below 400 KB.

After a change to `engine.ts` or to the generator, run `npm run vectors` in the `web` folder, then change `TableEngine.cs` until the C# tests pass.
Without the update, `npm test` fails when a file is different.
