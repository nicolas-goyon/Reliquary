# 0041. The API reads a deck from a link of Moxfield or Archidekt

- Status: Accepted
- Date: 2026-11-06
- Deciders: @nicolas-goyon

## Context

Users want to import a deck from its link. Before this decision, they had to export the deck as text on the other site and paste the text.

- Archidekt has an open API for public and unlisted decks.
- Moxfield has no public API. Cloudflare blocks the requests of a server. Moxfield support can add a user agent to an allow list.
- The browser cannot read these APIs, because they do not allow requests from other sites.

## Decision

- The endpoint `POST /decks/import-link` reads the deck on the server and uses the usual import.
- The API accepts only the deck pages of Moxfield and Archidekt.
- The API makes the address of the request from the ID of the deck. Thus a user cannot send a request to another server.
- The HTTP client does not follow redirects.
- The setting `Decks:LinkImport:MoxfieldUserAgent` enables Moxfield.
- When the setting is empty, the API gives the error `deck_link_site_unavailable`. The interface then tells the user to paste the text.
- The API converts the data of the other site to the deck text format, so the import of a link and the import of a text give the same result.

## Consequences

- An instance can read Moxfield only after an agreement with Moxfield support.
- The format of the Moxfield API is not documented. The converter accepts the boards of the versions 2 and 3, but it can stop working when Moxfield changes its API.
- Each import from a link sends one request to the other site.
