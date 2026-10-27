# Deck text format

This page gives the text format of a deck.
The text mode of the deck editor, the copy of a deck and the text import use it.
The format accepts the bulk edit syntax of Moxfield.
Terms follow the [glossary](../glossary.md).

## Example

```text
Commander
1 Atraxa, Praetors' Voice (2X2) 190 *F*

Main
1 Sol Ring (C21) 263 #Ramp #!Mana Rock -- Turn one
10 Forest
1 Fire // Ice *E*

Considering
1 Cultivate #Ramp
```

## Sections

A section header gives the zone of the next lines.
The header can have a colon at the end, a number of cards in parentheses, or `//` at the start.

| Header | Zone |
| --- | --- |
| `Commander`, `Commanders` | Commander |
| `Companion` | Companion |
| `Main`, `Deck`, `Main deck`, `Mainboard` | Main deck |
| `Sideboard`, `Side` | Sideboard |
| `Considering`, `Maybeboard`, `Maybe` | Considering |
| `Tokens`, `Token` | Tokens |

The lines before the first header go to the main deck.
The parser ignores the empty lines and the lines that start with `//`.

## Lines

A line has these parts, in this order:

| Part | Example | Note |
| --- | --- | --- |
| Quantity | `1`, `4x` | Optional. The default is 1. From 1 to 999. |
| Card name | `Sol Ring`, `Fire // Ice`, `Foudre` | Necessary. Any card name: Oracle name, face name, localized name or alternate name. |
| Set code and collector number | `(C21) 263`, `(CMR)` | Optional. The collector number is optional. |
| Finish | `*F*`, `*E*` | Optional. `*F*` gives the foil finish, and `*E*` gives the etched finish. |
| Local tags | `#Ramp`, `#Fast mana` | Optional. A tag ends at the next `#` or at the note. |
| Global tags | `#!Mana Rock` | Optional. |
| Note | `-- Turn one` | Optional. The note starts after ` -- `. |

## Errors

The parser reports each line that it cannot read, with the line number and a reason code:

| Code | Reason |
| --- | --- |
| `missing_name` | The line has no card name. |
| `quantity_out_of_range` | The quantity is not from 1 to 999. |
| `tag_too_long` | A tag has more than 50 characters. |

The deck editor shows these errors before it saves the deck.
