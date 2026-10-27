# Glossary

This glossary gives one meaning to each project term.
Use each term with this meaning only.
Do not use a different word for a term in this list.
The French column gives the term for the French interface.

## Cards

| Term | Meaning | French interface |
| --- | --- | --- |
| Oracle card | The logical card. All printings of a card share one Oracle card, with one rules text, one set of legalities and one color identity. | Carte |
| Printing | One edition of an Oracle card. A printing has a set, a collector number, a language, finishes and external IDs. | Impression |
| Card face | One face or one half of a card. Double-faced, split, flip and meld cards have two or more faces. Rooms, Adventures and prepared creatures also have two parts. | Face |
| Card name | A name that identifies an Oracle card. Card names include localized names, alternate names of special printings and face names. | Nom de carte |
| Token | A card that represents a token from a game effect. A deck lists its tokens in the tokens zone. | Jeton |

## Decks

| Term | Meaning | French interface |
| --- | --- | --- |
| Deck | A named list of cards for one format. A deck has zones, tags and versions. | Deck |
| Zone | A part of a deck: commanders, companion, main deck, sideboard, considering or tokens. | Zone |
| Considering | The zone for cards that the user thinks about for the deck. Other tools call it the maybeboard. | À l'étude |
| Deck line | One line of a deck version: an Oracle card, a quantity, a zone, tags, a note and purchase preferences. | Ligne |
| Deck version | A frozen copy of a deck list, with a message, a date and parent versions. A deck version never changes, like a Git commit. | Version |
| Variant | An alternative list of a deck that starts from one deck version. Examples: a budget list, a cEDH list, an anti-aggro list. | Variante |
| Assembled deck | A deck that exists as physical cards. Its cards are in a location of the type "assembled deck". | Deck monté |
| Assembled version | The deck version that agrees with the physical cards of an assembled deck. | Version montée |
| Tag | A label on a deck line. A global tag exists in all the decks of a user. A local tag exists in one deck only. | Tag global, tag local |
| Category | A predefined tag that the analysis uses: ramp, card draw, targeted interaction, board wipe, synergy, land, win condition or protection. | Catégorie |
| Auto-tag | A feature that suggests tags for deck lines. It uses Scryfall Tagger data and rules on the card text. | Tag automatique |
| Visibility | Who can open a deck: only the owner (private), people with the link (unlisted) or everyone (public). | Visibilité : privé, non listé, public |
| Purchase preferences | The rules of one deck line for purchases: wanted or avoided printings, languages, minimum condition, foil and maximum price. | Préférences d'achat |
| Purchase list | The cards to buy to complete a deck or a variant. It is the deck list minus the available cards. | Liste d'achat |
| Transition plan | The steps from the assembled version to a variant: cards to remove from the deck, cards to take from a location and cards to buy. | Plan de transition |
| Commander | The Magic: The Gathering format, also known as EDH. Write "commander" in lowercase for the card in the command zone. | Commander |
| Bracket | The Commander scale for the power level of a deck. Reliquary shows an indicative bracket. | Bracket |
| Game Changer | A card on the official Commander list of cards that change games a lot. The number of Game Changers limits the bracket of a deck. | Game Changer |
| Modal land | A modal double-faced card with a spell face and a land face. The analysis counts it as a land and as a spell. | Terrain modal |
| Mana source | A card that produces mana of a color: a land, a modal land or a permanent with a mana ability. | Source de mana |
| Category need | The comparison of the number of cards in a category with a target for the format. | Besoin |

## Collection

| Term | Meaning | French interface |
| --- | --- | --- |
| Collection | All the cards that a user owns. The collection is the sum of all location entries. | Collection |
| Location | A physical place that holds cards: rack, binder, storage box, assembled deck or cube. | Emplacement |
| Section | An optional part of a location, for example a box in a rack. A location has two levels at most: the location, then the section. | Boîte |
| Location entry | One line of a location: an Oracle card, a quantity, a language, an optional printing and optional advanced fields. | Entrée |
| Locked location | A location whose content cannot change. The user must unlock the location before any change to its content. | Emplacement verrouillé |
| Pickable location | A location whose cards are available cards. A location that is not pickable can change, but its cards are not available cards. | Emplacement piochable |
| Available cards | The cards that can complete a deck. They are the cards in pickable locations, and the cards of collections that other users share with you, if you enable this option. The cards of an assembled deck are never available cards. | Cartes disponibles |
| Inventory | A dated record of the content of one location. | Inventaire |
| Movement | A record of cards that enter a location, leave a location or go to another location. | Mouvement |
| Advanced collection | A setting that adds optional fields to location entries: foil, condition, purchase price, entry date and notes. | Collection avancée |
| Collection share | An access that a user gives to another user on some locations, with read, difference or edit rights. | Partage de collection |

## Deck testing

| Term | Meaning | French interface |
| --- | --- | --- |
| Table engine | The engine that all deck testing modes share: zones, draw, shuffle, tokens, counters and flip. It has no automated game rules. | Moteur de table |
| Goldfish | A test mode for one player with no opponent. | Goldfish |
| Local multiplayer | A test mode where one person plays 3 or 4 decks. | Multi local |
| Online game | A test mode for several people on different computers. | Partie en ligne |
| Game session | The real-time connection group of one online game. | Salon |
| Shared screen | A test mode with a table window and a player window, for screen sharing, a second screen or a stream. | Affichage partagé |
| Table window | The window that shows the table. It masks hidden information. | Fenêtre table |
| Player window | The private window that shows the hand of one player. | Fenêtre joueur |

## Accounts

| Term | Meaning | French interface |
| --- | --- | --- |
| Account | The identity of a user on an instance. The ID of the account is also the ID of the owner of the user data. | Compte |
| Administrator | A user who manages the instance. The setting `Auth:AdministratorEmails` gives the administrators. | Administrateur |
| Web session | A sign-in in the web interface. It has a short access token and a refresh token in a cookie. | Session |
| Access token | A signed token that gives access to the public API for 15 minutes. | Jeton d'accès |
| Refresh token | A secret in an HttpOnly cookie that gives a new access token. Each use replaces it. | — |
| OAuth login | A connection between an account and an account at an OAuth provider: Google, GitHub or Discord. The user can then sign in with that provider. | Connexion externe |
| API key | A personal secret that a script or a third-party client uses to call the public API for a user. | Clé d'API |
| Quota | The maximum number of requests of an API key or of an account in one day (UTC). | Quota |

## Extensions

| Term | Meaning | French interface |
| --- | --- | --- |
| Core | The Reliquary application without extensions. | Cœur |
| Core component | An internal part of the core: Accounts, Cards, Decks, Collection or Playtest. A core component owns its tables. It communicates with other core components only through interfaces and domain events. | — |
| Extension | A plugin or a module. | Extension |
| Plugin | An extension that runs only in the browser, in a sandbox. Anyone can write a plugin. Each user installs plugins for their own account. | Plugin |
| Module | A trusted extension with a front-end part and a server part. Each module has a signature. Only the instance administrator can install a module. | Module |
| Plugin manifest | The JSON file of a plugin. It declares the ID, version, permissions, slots, network domains, linked accounts and entry point. | Manifeste |
| Slot | A named place in the web interface. The core, plugins and modules add interface elements to slots. | Slot |
| Linked account | A connection between a Reliquary account and an account on an external site. Plugins and modules use linked accounts. | Compte lié |
| Plugin catalog | The public list of plugins that the project reviewed and signed. | Catalogue de plugins |
| Extension registry | The list of the extensions that the administrator installed on the instance, with an activation flag for each extension. | Registre des extensions |
| Core API version | The version of the API that the core gives to extensions. A manifest gives the versions that the extension supports, for example `^1.0`. | Version de l'API du cœur |

## Platform

| Term | Meaning | French interface |
| --- | --- | --- |
| Instance | One installation of Reliquary, with its databases. | Instance |
| Owner | The user who owns a piece of user data. Each table of user data has an `owner_id` column. | Propriétaire |
| Shard directory | A table that gives the database of each user or range of users. | — |
| Domain event | A versioned record of a change in a core component or a module. | — |
| Outbox | A table where a core component writes domain events in the same transaction as its data change. A publisher then sends the events to the consumers. | — |
| Migration set | An ordered list of SQL scripts that one owner applies to one schema. The owner is the platform, a core component or a module. | — |
| Scheduled job | A task that the worker runs again after the same interval each time, for example the daily import of the card data. | — |
| Worker | The process that runs the outbox publisher, the scheduled jobs and the event consumers. It runs separately from the API. | — |
| Public API | The versioned HTTP API for the web interface, plugins and third-party clients. | API publique |
| Internal API | The versioned API between modules and the core. It is separate from the public API. | — |
| Milestone | A group of features with a gate at the end: M0 to M4, then Later. | Jalon |
| Gate | A criterion that a test can prove at the end of a milestone. Work on the next milestone starts after the gate passes. | Porte |

## Word choices

Use one verb for one action.
The words in the second column have the same meaning as the words in the first column, so do not use them.

| Use | Do not use | Meaning |
| --- | --- | --- |
| `check` | `verify`, `validate`, `confirm` | Make sure that something is correct. |
| `show` | `display` | Make something visible to the user. |
| `get` | `retrieve`, `fetch`, `obtain` | Receive data from a source. |
| `start`, `stop` | `launch`, `begin`, `initiate`, `halt`, `terminate` | Make a process run, or make it end. |
| `change` | `modify`, `alter` | Make something different. |
| `use` | `utilize`, `employ` | Put something into service. |
| `send` | `transmit` | Move data to a receiver. |
| `remove` | `take off`, `take out` | Take a card out of a deck or a location. The card continues to exist. |
| `delete` | `erase` | Destroy data, for example an account or a deck. |
| `create` | `set up`, `spin up` | Make a new item. |
