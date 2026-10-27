# Auto-tag fixtures

These files are the data of the acceptance test of auto-tag.
[Auto-tag](../../../../docs/architecture/auto-tag.md) tells how the test uses them.

- `reference-decks.json` contains 10 reference decks with the expected categories of each card.
  A person wrote the expected categories before the rules existed.
  `categories` gives the categories that a suggestion must contain.
  `accepted` gives other categories that a suggestion can also contain.
- `cards.jsonl` contains the cards of the reference decks, from `data/demo`, in the format of the Scryfall bulk files.
  The extract keeps only the fields that auto-tag reads, for example the layout, the type lines, the rules texts and the faces.
- `oracle_tags.jsonl` contains the Oracle tags of these cards and all their ancestors, from `data/demo`.
  The extract keeps only the links to the cards of `cards.jsonl`.

To add a card, copy its card and its tags from `data/demo` with the same fields, then add the card to a reference deck.

The card data comes from [Scryfall](https://scryfall.com).
Wizards of the Coast owns the content of the cards.
