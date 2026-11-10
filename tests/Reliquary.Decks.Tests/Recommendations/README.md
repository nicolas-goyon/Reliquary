# Recommendation samples

The file `recommander-atraxa.json` is a sample response of the public API of [recommander.cards](https://recommander.cards/api) for the commander Atraxa, Praetors' Voice.
A contributor wrote it by hand in the format of the API documentation.
It is not a copy of a real response.

The cards are cards of the card fixtures, and one card that the fixtures do not contain.
The tests use the sample with a fake HTTP handler, so they send no request to recommander.cards.

## Recorded responses

These files are real responses of `POST https://api.recommander.cards/public-release/api/decks/recommend/top`.
A contributor recorded them on 2026-11-10 with the body `{"card_format":"oracle_id","commander":"<Oracle ID>","deck":[]}`.

| File | Request | Status |
| --- | --- | --- |
| `recommander-recorded-atraxa.json` | The Oracle ID of Atraxa, Praetors' Voice | HTTP 200 |
| `recommander-recorded-unknown-commander.json` | An Oracle ID that is not a card | HTTP 400 |

The tests read these files to make sure that the reader accepts the real format of the API.
The list of a real response is in `data.recommendations`.
To record a new response, send the same request and replace the file.
