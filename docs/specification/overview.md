# Overview

This file gives the summary of Reliquary, the project name, the vision and the license.
It also gives the users, the rules between decks and the collection, and the scope decisions.
Terms follow the [glossary](../glossary.md).

## Summary

Reliquary is an open-source manager for Magic: The Gathering decks and collections, under the AGPL-3.0 license.
Reliquary is at the level of Moxfield from the start.
Commander is the priority format.

- **Core**: the core contains deck building, deck testing, public deck exploration, organization, analysis, versions and variants, and the collection by location.
  Deck testing has modes from goldfish to simplified online games.
- **What makes Reliquary different**:
  - variants of one deck, with a transition plan between a variant and the assembled version
  - an assembled deck that Reliquary treats as a location of the collection
  - locked locations, and locations that are not pickable
  - dated inventories
  - local multiplayer tests, and a shared screen for Discord or a stream
- **Extensions**: anyone can write browser plugins, and the plugin catalog lists the plugins that the project checked.
  The project delivers trusted server modules later, but the core prepares for them from [milestone](../roadmap.md) M0.
  The [extensions](../architecture/extensions.md) document gives the details.
- **Technology**: Reliquary uses C# and ASP.NET Core, TypeScript, and PostgreSQL.
  The API has no state, so Kubernetes can run replicas of the API.
  Reliquary can split the database by user, and move the data of each user to another database.
  One command creates a local instance.
  The [technology stack](../architecture/tech-stack.md) gives the details.
- **Roadmap**: the work has five [milestones](../roadmap.md), from M0 to M4.
  M1 is the minimum viable product (MVP) for Commander decks.

## Project name

The project name is **Reliquary**: the reliquary where you keep your cards, decks and collection.
Extensions keep the standard name **plugins**, so that users understand the name immediately.
The project keeps three other names in reserve: Tutor, Folio and Brewkit.

## Vision and principles

Reliquary is a manager for Magic: The Gathering decks and collections.
It is complete from the start, at the level of Moxfield, and it is open source.
Plugins extend Reliquary: they connect it to other tools or add elements to the interface.

### Goals

1. Replace Moxfield and Archidekt to build, analyze, test and share decks, with Commander as the priority format.
2. Connect the decks to the physical collection: what I own, where I store it and what I am missing.
3. Let people add integrations and information without a change to the core, through plugins and trusted modules.
   Anyone can write a plugin.

### Principles

- **A complete core, extensions for the rest**: the core contains all the features that users expect from a tool like Moxfield.
  Plugins run in the browser, and anyone can write them.
  Plugins add integrations, information and automations.
  Each module is a trusted extension with a front-end part and a server part.
  Modules add advanced features without making the core bigger.
- **API first**: the web interface uses the same public API as the plugins and the third-party clients.
- **Accounts and open data**: each user has an account, but can export all their data at any time, in open formats.
- **Scalable by design, simple to start**: one command creates a local instance.
  Later, the architecture can accept Kubernetes replicas and a split of the database, without a rewrite.
  The [technology stack](../architecture/tech-stack.md) gives the requirements.
- **Respect for the sources**: Reliquary gets the card data from Scryfall and MTGJSON, and follows their terms.
  Reliquary shows the card images from the URLs of these sources, and does not host copies of the images.
- **License**: AGPL-3.0.
  The [license comparison](#license) gives the reasons.

### License

Reliquary uses the AGPL-3.0 license, and not MIT or Apache 2.0.
This table compares the three licenses.

| Criterion | MIT | Apache 2.0 | AGPL-3.0 |
| --- | --- | --- | --- |
| Use, changes and commercial use | Yes | Yes | Yes |
| A fork can become closed source | Yes | Yes | No: the derived code stays under AGPL |
| A changed web service must publish its code | No | No | Yes, for all users of the online service |
| Protection against patent lawsuits | No | Yes (explicit patent license) | Yes |
| Obstacle to adoption by companies | None | None | Real: many companies avoid it |

The project chose AGPL-3.0 for these reasons:

- The project stays open.
- Other people can continue the project if its author stops.
- No fork of the project can become closed source, even as SaaS (software as a service).

The project can publish the plugin SDK separately under MIT or Apache 2.0, so that plugins can have their own license.
The choice between MIT and Apache 2.0 for the plugin SDK is still open.
If the project considers a paid offer one day, it will need a contributor license agreement (CLA).
With a CLA, the project keeps the possibility of a dual license.

## Users and use cases

Five profiles cover most of the needs.
The MVP first targets the Commander player who builds and tests decks.

| Profile | Main need | Example |
| --- | --- | --- |
| Player-brewer | Build, test, compare and iterate on decks | "I test 3 variants of my Commander deck against 3 decks of the group. Then I compare the variants with the assembled version." |
| Collector | Know what they own, where it is, and in which language | "Where is my Sol Ring? Rack 2, blue section." |
| Buyer | Know what to buy to complete a deck | "I need 14 more cards that match my criteria. I export the list to a marketplace." |
| Group of friends | Share collections to help each other | "I need 5 more cards, and Léa has 3 of them." |
| Developer | Deploy an instance and write plugins | "I install the tool for development, and I develop a plugin on my computer." |

## Deck and collection rules

These rules connect decks and the collection.

- An **assembled deck is part of the collection**.
  It is a location, like a rack, and it contains its own cards.
- An assembled deck is **never a source** to complete another deck.
  In the difference between a deck and the collection, its cards are never available cards.
- The user can mark any location as **locked**, as **not pickable**, or both:
  - The user must unlock a locked location before any change to its content.
    Examples of locked locations: a cube, a collection binder or a showcase deck.
  - The content of a location that is not pickable can change, but its cards are not available cards.
- The **purchase list** is the difference between a deck (or a variant) and the available cards.
  The available cards are the cards in the pickable locations of the user's collection.
  If the user enables this option, the available cards also include the cards of collections that other users share with the user.

## Scope decisions

The project made these product decisions about the scope of Reliquary.

| Subject | Decision |
| --- | --- |
| Priority format | Commander |
| Card loans | Out of scope |
| Game tracking | A separate tool, which a plugin connects to Reliquary |

The [decision records](../decisions/README.md) give the reasons for the decisions of the project.
