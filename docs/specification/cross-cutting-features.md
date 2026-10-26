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
