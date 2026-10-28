# Cross-cutting features

This file lists the features that are not a separate feature area.
Users expect these features everywhere in Reliquary.
Terms follow the [glossary](../glossary.md).

## Feature list

The **Priority** column gives the priority of each feature: MVP, v1, v2 or Later.
The **Milestone** column gives the [milestone](../roadmap.md) of each feature.

| Feature | Detail | Priority | Milestone |
| --- | --- | --- | --- |
| Accounts, authentication (email and OAuth), profiles | Necessary as soon as public decks exist | MVP | M0 |
| Personal API keys and quotas | Each user can create keys. Each key and each account has a daily quota, for example 10,000 requests a day. If the project grows, requests above the quota can become billable. | MVP (quotas), Later (billing) | M0 (keys and quotas), Later (billing) |
| Automatic update of the card data | New sets, errata and bans | MVP | M0 |
| French and English interface, localized card names | For a French-speaking public and an international public | MVP | M0, then every [milestone](../roadmap.md) |
| GDPR (account export and deletion), instance backup and restore | Mandatory for a public instance in Europe | MVP | M1 |
| Sharing (link, deck image, embed) | To share decks on Discord and on forums | v1 | M1 (link), M3 (image and embed) |
| Notes for each card in a deck ("why this card is here") | A record of the theorycraft | v1 | M2 |
| Printable proxies (PDF, A4 and Letter) | Test a variant before a purchase | v1 | M2 |
| Mobile PWA (progressive web app) | Organize the racks with a phone in hand | v1 | M2 |
| Keyboard shortcuts, dark mode, accessibility | Comfort for frequent users | v1 | M2 |
| Moderation of public content, reports | As soon as a public instance opens | v1 | M2, before a public instance opens |
| Comments and discussion under a deck | Discussions about a public deck | v2 | Later |
| Deck co-editing | Use by a group | Later | Later |
| Notifications (for example, a banned card in one of my decks) | Continuous added value | Later | Later |

## Printable proxies

The **Print proxies** action of the deck page makes a PDF file of [proxies](../glossary.md) for each user who can see the deck.
The PDF has 9 cards on each page, at the real size of 63 × 88 mm, with cut marks, on A4 or Letter paper.
[ADR 0038](../decisions/0038-proxy-pdf-in-the-browser.md) tells how the web app makes the PDF.

- The user prints all the cards of the deck, or the deck lines that the user selects.
  All the cards of the deck are the commander, companion and main zones. The user can add the sideboard, considering and tokens zones.
  Each line gives as many proxies as its quantity.
- An option adds the back face of each double-faced card, after its front face.
  Only a printing with one image for each face has a back face image.
- The default paper is Letter for a browser language of the United States, for example `en-US`, and A4 for other languages.
- A card with no image, or with an image that does not load, gets a frame with its name.
  After the PDF, the dialog gives the names of the cards whose image did not load.
- The user must print the PDF at 100% scale, so that each card has the real size.
