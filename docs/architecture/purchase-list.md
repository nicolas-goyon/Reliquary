# Purchase list

This page tells how the Decks component records the purchase preferences and calculates the purchase list.
Terms follow the [glossary](../glossary.md).
The [deck building specification](../specification/deck-building.md#purchase-preferences) gives the features.
The [rules between decks and collection](../specification/overview.md#deck-and-collection-rules) give the available cards.

## Purchase preferences

Each deck line can have purchase preferences.
The column `purchase_preferences` (`jsonb`) of `content.deck_lines` contains them. No value means that the line accepts each copy.

| Field | Content |
| --- | --- |
| `wantedPrintingIds` | The printings that the user wants. An empty list accepts each printing that is not avoided. |
| `avoidedPrintingIds` | The printings that the user does not want. |
| `languages` | The languages that the user accepts, for example `fr` and `en`. An empty list accepts each language. |
| `minimumCondition` | The worst condition that the user accepts: `mint`, `near_mint`, `excellent`, `good`, `light_played`, `played` or `poor`. |
| `foil` | `any`, `foil` or `nonfoil`. The finish `etched` is a foil finish. |
| `maximumPrice` | The highest price of a purchase, with an amount and a currency code. Only the exports to the marketplaces use it. |

- `PATCH /api/v1/decks/{id}/lines/{lineId}` sets the preferences with `purchasePreferences`, and removes them with `clearPurchasePreferences`.
  The new object replaces all the preferences of the line.
- The API makes the languages lowercase and removes the duplicates.
- A version keeps the preferences of each frozen line ([ADR 0033](../decisions/0033-deck-versions-and-variants.md)).
  A variant, a copy, a restore and a merge keep the preferences of the lines.

## Purchase list

`GET /api/v1/decks/{id}/purchase-list` gives the purchase list of a deck or a variant of the user.
The endpoint needs the scope `collection:read`, because the response shows the locations of the collection.

The list has the lines of the commander, companion, main and sideboard zones.
With `includeTokens=true`, it also has the lines of the tokens zone. The considering zone is never in the list.

The Decks component reads the copies of the collection through `ICollectionCatalog` ([ADR 0034](../decisions/0034-collection-model.md)).
`PurchasePlanner` calculates the list with these rules:

1. The copies in the location of the deck are already in the deck. For a variant, the copies in the location of its main deck also count.
2. The other copies are available when their location is pickable and is not an assembled deck.
3. A copy fits a line when it agrees with the purchase preferences of the line.
   A copy with no printing, no finish or no condition agrees with the preferences, except with a wanted printing.
4. The lines with the most preferences take their copies first, so that a line with no preference takes the copies that stay.
5. A line takes the copies of the unlocked locations before the copies of the locked locations.
   The user must unlock a locked location before the cards move.
6. The cards that stay are the cards to buy.

When a line has cards to buy, the response gives the **closest copies**, with the reasons.
The closest copies are the free copies of the card that do not fit the line.
The reasons are `printing`, `language`, `finish`, `condition` and `unavailable`.
The reason `unavailable` is for a copy in another assembled deck or in a location that is not pickable.
The copies with fewer reasons come first, and an available copy comes before an unavailable copy.
For example, a line that wants a French copy shows "1 English copy in Rack 2" with the reason `language`.

## Gate of milestone M2

The test `PurchaseListGateTests` checks the purchase list of a reference dataset, card by card.
The dataset has copies in the location of the deck and in another assembled deck.
It also has copies in a locked location and in a location that is not pickable.
Each reason of a closest copy is in the dataset.
A change of the rules above must change the dataset and the expected results together.
