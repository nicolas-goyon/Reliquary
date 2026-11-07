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

## Transition plan

`GET /api/v1/decks/{id}/transition-plan` gives the physical steps from the assembled version to the working list of a variant.
For a main deck, the target is its own working list.
The endpoint needs the scope `collection:read`.
When the main deck is not an assembled deck, the API gives the error `deck_not_assembled` (409).

1. The plan compares the cards of the commander, companion, main and sideboard zones of the two lists, by Oracle card.
   A change of zone, printing or finish only is not a step.
2. The cards that the target has fewer times are the **cards to remove** from the assembled deck.
3. For the cards that the target has more times, `PurchasePlanner` gives the **cards to take** from the collection and the **cards to buy**.
   The location of the assembled deck is not a source, because its cards are already in the plan.
4. The response gives the locked locations of the cards to take in `unlockFirst`.
   The user must unlock these locations before the cards move.

The plan does not change the collection. After the physical steps, the user moves the cards and marks the new assembled version.

The web app shows the plan on the page `/decks/{id}/transition-plan`, with a check box for each step.
The browser keeps the checked steps of each assembled version in its local storage.
The history panel of a main deck marks a version as the assembled version, and releases the assembled deck.

## Web app

The owner of a deck sets the purchase preferences of a line on the deck page, with the "Preferences" button of the line.
The dialog loads the printings of the card from `GET /cards/{id}/printings`.
A line with purchase preferences shows a marker.

The page `/decks/{id}/purchase-list` shows the purchase list of a deck or a variant.
The deck page gives a link to it for the owner.
The table gives the cards in the deck, the cards from the collection and the cards to buy of each line.
A copy in a locked location shows a message that tells the user to unlock the location first.
The page writes each closest copy as a sentence from its reasons and the preferences of the line.
For example, the page shows "Not in French: 1 English copy in Rack 2".

The web app makes the exports of the cards to buy, with no request to the API.
The functions are in `web/src/purchases/exports.ts`.
The exports add the cards to buy of the same card in different zones into one line, and use the English card name.

| Format | Line | Use |
| --- | --- | --- |
| Cardmarket wants list | `<quantity> <card name>` | Paste the text in a wants list of Cardmarket. |
| TCGplayer mass entry | `<quantity> <card name> [SET]` | Paste the text in the mass entry of TCGplayer. The set code is only on a card whose lines want exactly one printing. |
| Plain text | `<quantity> <card name> (SET number, max price)` | The printing and the maximum price are only on the cards that have them. |

The text formats of the marketplaces cannot give a maximum price, so only the plain text shows it.
The user copies the text to the clipboard or downloads it as a `.txt` file.
The page also prints proxies of the cards to buy with the PDF function of the deck page ([ADR 0038](../decisions/0038-proxy-pdf-in-the-browser.md)).

## Gate of milestone M2

The test `PurchaseListGateTests` checks the purchase list of a reference dataset, card by card.
The dataset has copies in the location of the deck and in another assembled deck.
It also has copies in a locked location and in a location that is not pickable.
Each reason of a closest copy is in the dataset.
A change of the rules above must change the dataset and the expected results together.
