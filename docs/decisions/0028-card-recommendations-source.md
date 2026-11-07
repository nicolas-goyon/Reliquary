# 0028. Source of the card recommendations

- Status: Accepted
- Date: 2026-10-27
- Deciders: @nicolas-goyon

## Context

The [specification](../specification/deck-building.md) asks for card recommendations for the commanders of a deck.
[ADR 0012](0012-card-data-sources.md) names EDHREC as the source, and [ADR 0020](0020-card-data-import.md) names recommander.cards as an optional source.
The spike [#98](https://github.com/nicolas-goyon/reliquary/issues/98) checked the terms of each source, its data, its limits and the cache that Reliquary needs.
The spike found these facts on 2026-10-27.

### EDHREC

- EDHREC has no official public API.
  The library pyedhrec says: "Currently, EDHREC does not provide an API".
- Many tools read the JSON pages of the site, for example `https://json.edhrec.com/pages/commanders/<slug>.json`.
  The site does not document these pages.
- The [terms of service of EDHREC](https://edhrec.com/terms) (Space Cow Media, effective date 6 August 2024) give a license "solely for your own personal, noncommercial use".
- The same terms forbid these actions:
  - to copy, reproduce, distribute, republish, download or show a part of the site (the terms say `displayed`), unless the terms say otherwise
  - to generate automated searches, requests or queries to the site
  - to access the site in order to build a similar or competitive website
- The terms give no exception for the JSON pages, and EDHREC publishes no data license.

A self-hosted instance that reads the JSON pages makes automated requests to the site.
When it shows the data to its users, it also shows a copy of the content of the site.
The terms forbid both actions.
A written agreement with Space Cow Media can change this result, but the project has no agreement.

### recommander.cards

- The site is recommander.cards, with an "a".
  Its [public API](https://recommander.cards/api) has one endpoint: `POST https://api.recommander.cards/public-release/api/decks/recommend/top`.
- The request gives a commander, an optional partner and an optional deck list.
  The cards are Oracle IDs (default), Scryfall IDs or names.
- The response gives a list of cards with the Oracle ID, the name and a score.
  The API returns only the cards with a score above 0.7.
- The documentation shows two shapes for the list: `data` is the list, or `data.recommendations` is the list.
- The API needs no key.
  It has rate limits, but the documentation gives no numbers.
  Over the limit, the API answers with the HTTP status 429 or with the result code `error_rate_limited`.
- Other result codes include `error_not_found`, `error_booting` and `error_model_loading`.
- The terms of the API page give these conditions:
  - Use the data for personal and non-commercial use. For commercial use, contact Recommander.
  - Give "appropriate attribution to Recommander as the source of the recommendations".
  - Do not exceed the rate limits.
  - Do not scrape the website. Use the API.
- The model learns from public Commander deck lists.
  Other deck builders use the API with a credit, for example [DeckCheck](https://deckcheck.co/blog/deckcheck-x-recommander).

### Other options

- [Commander Spellbook](https://commanderspellbook.com/) finds combos.
  Its [backend](https://github.com/SpaceCowMedia/commander-spellbook-backend) has the MIT license.
  It does not recommend cards for a commander, and the combo detection of [#103](https://github.com/nicolas-goyon/reliquary/issues/103) uses it.
- The Scryfall data gives `edhrec_rank`, the global popularity of a card on EDHREC.
  Reliquary imports it already for `order:edhrec`.
  The rank does not depend on the commander, so it cannot recommend cards for a deck.
- A local model can count the cards that occur together in the public decks of the instance.
  It needs no external service.
  A new instance has few public decks, so the model has no useful result at the start.
  It is a possible addition after milestone M1.

### Cache and limits

- Reliquary asks for the recommendations of the commanders only, with no deck list.
  The same commanders then give the same result for all the decks, so a cache entry serves many decks.
  The deck list of the user also stays on the instance.
- Reliquary removes the cards of the deck after it reads the cache.
- The recommendations of a commander change slowly.
  A cache of 24 hours keeps the requests to the API low, and the data stays recent.

### Sources

The spike read these pages on 2026-10-27:

- [EDHREC terms of service](https://edhrec.com/terms)
- [EDHREC FAQ](https://edhrec.com/faq)
- [pyedhrec on PyPI](https://pypi.org/project/pyedhrec)
- [recommander.cards public API](https://recommander.cards/api)
- [DeckCheck and Recommander](https://deckcheck.co/blog/deckcheck-x-recommander)
- [Commander Spellbook backend](https://github.com/SpaceCowMedia/commander-spellbook-backend)

## Decision

- The card recommendations use the public API of recommander.cards.
- Reliquary does not use the JSON pages of EDHREC, because the terms of EDHREC forbid automated requests and copies of their content.
  Reliquary can add EDHREC only after a written agreement with Space Cow Media, and a new ADR records it.
- The administrator enables the recommendations with the setting `Recommendations:Enabled`.
  The default is `false`.
- The interface shows the credit "Recommendations by Recommander" with a link to recommander.cards in each place that shows recommendations.
  The API response gives the name and the address of the source, so the other clients can show the credit too.
- The Decks component owns the recommendations, in the folder `Recommendations`.
  The recommendations need the lines of the deck and the card catalog, and they add no table.
  A new core component is not necessary.
- The endpoint `GET /api/v1/decks/{id}/recommendations` gives the recommendations to the owner of the deck.
- Reliquary sends only the Oracle IDs of the commanders and of the partner, with `card_format` set to `oracle_id`.
- Redis keeps the list of each commander pair for 24 hours (`Recommendations:CacheDuration`).
  All the API replicas share the cache.
- All the API replicas share one rate limit in Redis: 20 requests to recommander.cards in each minute (`Recommendations:RequestsPerMinute`).
- After a failure, a 429 status or an unknown commander, Redis keeps the result for 10 minutes (`Recommendations:FailureCacheDuration`).
  A 429 status with a `Retry-After` header keeps the result for the time of the header, if that time is longer.
- A request to recommander.cards stops after 5 seconds (`Recommendations:Timeout`).
- When the source fails, the endpoint answers with the status `unavailable` and no cards.
  The rest of the deck page works as usual.
- The administrator of a commercial instance must get an agreement from Recommander before the instance enables the recommendations.

## Consequences

- The recommendations come from a cache after the first request for a commander pair.
  The response time is then the time of a Redis read and of a query of the card catalog.
- The first request for a commander pair waits for recommander.cards, 5 seconds at most.
- The recommendations do not use the cards of the deck, so they are less personal than the recommendations of the recommander.cards site.
- The recommendations depend on a free service that gives no guarantee.
  The endpoint must handle all the failures, and the tests use a recorded sample response, never the real service.
- The two shapes of the response list need a reader that accepts both.
- The specification and the data model no longer name EDHREC as the source of the recommendations.
