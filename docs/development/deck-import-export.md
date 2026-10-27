# Deck import and export

This page gives the file formats of the deck import and the deck export.
Terms follow the [glossary](../glossary.md).

## Endpoints

- `POST /api/v1/decks/import` makes a new deck from a file.
  The body gives the file format, the content of the file, and the name, the format and the visibility of the new deck.
- `GET /api/v1/decks/{id}/export?format=moxfield` gives a deck as a file: its name, its media type and its content.

The import accepts all the card names: Oracle names, face names, localized names and alternate names.

## Formats

| Format | Name in the API | Zones | Printings | Tags |
| --- | --- | --- | --- | --- |
| Reliquary text | `text` | All the zones | Set code and collector number | Local tags, global tags and categories |
| Moxfield | `moxfield` | Commander, Companion, Mainboard, Sideboard and Maybeboard | Set code and collector number | Local and global tags |
| MTG Arena | `mtga` | Commander, Companion, Deck and Sideboard | Set code and collector number | No |
| Magic Online text | `mtgo` | Main deck and sideboard, with an empty line between them | No | No |
| Magic Online XML | `dek` | Main deck and sideboard | No | No |
| Archidekt | `archidekt` | Categories Commander, Companion, Sideboard, Maybeboard and Tokens | Set code and collector number | The other categories become local tags. |

The [deck text format](deck-text-format.md) gives the syntax of the Reliquary text, which is also the syntax of Moxfield.

Some formats have fewer zones than Reliquary:

- The export to Moxfield leaves out the tokens zone.
- The export to MTG Arena leaves out the considering and tokens zones, the tags and the notes.
- In a Commander deck, the exports to Magic Online put the commanders in the sideboard, as Magic Online asks.

## Errors and warnings

The import makes no deck when a line has an error.
The error `deck_text_invalid` gives each line with its number, its reason code and its text.
The reason codes are the codes of the deck text format, and `card_not_found`, and `invalid_file` for an XML file that the import cannot read.

Other tools can use set codes that Scryfall does not use.
When the import finds no printing for a set code and a collector number, the line stays in the deck with no printing.
The response gives a warning with the code `printing_not_found` for this line.

## Gate of milestone M1

The test `MoxfieldRoundTripLosesNoData` imports a Moxfield deck, edits it, and exports it again.
It compares the quantities, the printings, the finishes, the zones and the tags of each line.
