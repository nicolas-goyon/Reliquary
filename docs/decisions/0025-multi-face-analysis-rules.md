# 0025. Analysis rules for cards with more than one face

- Status: Accepted
- Date: 2026-10-27
- Deciders: @nicolas-goyon

## Context

The specification of the [multi-face cards](../specification/multi-face-cards.md) asks for explicit analysis rules.
Issue [#36](https://github.com/nicolas-goyon/reliquary/issues/36) asks these questions:

- Which mana value goes into the mana curve: the value of the front face, or the Oracle value?
- Does a modal double-faced card with a land face count as a land?
- Which rules apply to split cards, Adventures, Rooms and prepared creatures?

The card data has a layout for each Oracle card, and a row for each card face ([ADR 0020](0020-card-data-import.md)).
The rules of the game give one mana value to each card.
For a split card and a Room, the mana value outside the stack is the total of the two halves.
For example, Fire // Ice has a mana value of 4, and Ticket Booth // Tunnel of Hate has a mana value of 9.
A player casts one half only, so the total is not useful in a mana curve.

The card search keeps the mana value of the rules, so the results agree with Scryfall.
The analysis has a different purpose: it tells the player when the deck can cast its cards.

## Decision

The analysis uses the **castable faces** of each card.
A castable face is a face that a player can cast or play from the hand.

| Layout | Castable faces | Example |
| --- | --- | --- |
| One face, and the other layouts | The card | Sol Ring |
| Transform, flip and meld | The front face | Delver of Secrets // Insectile Aberration |
| Modal double-faced | The two faces | Shatterskull Smashing // Shatterskull, the Hammer Pass |
| Split, with Rooms, aftermath and fuse cards | The two halves | Fire // Ice, Ticket Booth // Tunnel of Hate |
| Adventure and prepared creature | The two parts | Bonecrusher Giant // Stomp |

The **primary face** of a card is the first face.
For a split card, the primary face is the half with the lowest mana value.

The analysis applies these rules:

1. **Mana curve**: the curve uses the mana value of the primary face.
   For example, Fire // Ice and Shatterskull Smashing are at 2.
   Ticket Booth // Tunnel of Hate and Bonecrusher Giant // Stomp are at 3.
2. **Lands**: a card is a land when its first face is a land, for example a basic land, a pathway or Dryad Arbor.
   A land is not in the mana curve.
3. **Modal lands**: a modal double-faced card with a spell face and a land face is a modal land.
   A modal land counts as a land for the land count and for the mana sources.
   It also counts as a spell in the mana curve, with the mana value of its spell face.
   The analysis shows the modal lands separately, so the user sees both counts.
4. **Transformed lands**: a card that becomes a land only when it transforms is not a land, for example Search for Azcanta.
5. **Card types**: the type distribution counts each card one time for each card type of its castable faces.
   Bonecrusher Giant // Stomp is a creature and an instant.
6. **Colors**: the color distribution uses the colors of the castable faces.
   The back face of a transforming card does not add a color.
7. **Mana symbols**: the mana requirements (Karsten method) use the mana cost of the primary face.
   The costs of the other castable faces show as alternatives.
8. **Color identity and legality**: the checks use the Oracle color identity and the Oracle legalities.
   As the rules of the game say, they include all the faces.
9. **Categories and tags**: a category belongs to the Oracle card, not to a face. Auto-tag reads the text of all the faces.
10. **Extra cards**: the analysis does not count tokens, emblems, meld results and the other extra cards.

The Cards component calculates these facts for each Oracle card.
The other core components get them through the interface `ICardCatalog` of the building blocks.

## Consequences

- The mana value of a split card or a Room in the analysis is different from its mana value in the search. The documentation of the analysis tells this.
- A deck with modal lands shows more lands than cards with a land front face. The two counts are visible.
- The tests of the rules cover each layout of the decision table.
- A new layout uses the rules of a card with one face, until a new decision changes it.
