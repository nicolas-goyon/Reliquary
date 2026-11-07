# Reliquary

Reliquary is an open-source deck and collection manager for Magic: The Gathering.
Its target is the level of Moxfield from the first release, with Commander as the priority format.

> **Status:** early development.
> The project is in milestone M0 (foundations).
> Reliquary is not ready for use yet.

## What Reliquary does

- Build, analyze, test and share decks, with Commander first.
- Keep the versions and the variants of a deck, and compare them.
- Get a transition plan from the physical deck to a variant: cards to remove, cards to take from the collection, and cards to buy.
- Connect the decks to the physical collection: the cards that you own, their location, and the cards that you must buy.
- Treat an assembled deck as a location of the collection.
- Test decks alone, in local multiplayer, online, or with a shared screen for Discord and streams.
- Add features with plugins in the browser, and with trusted modules on the server.

## Documentation

| Document | Content |
| --- | --- |
| [Specification](docs/specification/README.md) | What Reliquary must do |
| [Architecture](docs/architecture/overview.md) | How Reliquary works |
| [Roadmap](docs/roadmap.md) | The milestones and their gates |
| [Decision records](docs/decisions/README.md) | The reasons for each important decision |
| [Glossary](docs/glossary.md) | The meaning of each project term |
| [Getting started](docs/development/getting-started.md) | How to run Reliquary on your computer |

The [GitHub milestones](https://github.com/nicolas-goyon/reliquary/milestones) and their issues give the progress of the work.

## Technology

- Backend: C# and ASP.NET Core on .NET 10, as a modular monolith
- Front end: TypeScript, React and Vite, with a French and English interface
- Data: PostgreSQL and Redis
- Real time: SignalR

## Quick start

To try Reliquary, you need Docker with Compose and [just](https://just.systems) only:

```shell
just up        # build and start a full instance with the demo data on http://localhost:8080
just down      # destroy the instance and its data
```

To change the code, you also need the .NET 10 SDK, Node.js 24 and Python 3:

```shell
just infra     # start PostgreSQL, Redis and Mailpit
just restore   # install the .NET and npm packages
just api       # start the API on http://localhost:5080
just web       # start the web app on http://localhost:5173 (in a second terminal)
```

The [getting started guide](docs/development/getting-started.md) gives all the steps.

## Contributing

All changes go through pull requests to the `develop` branch.
All English text follows ASD-STE100 Simplified Technical English.
The interface is in French and in English.
Read [CONTRIBUTING.md](CONTRIBUTING.md) before you start.

## License

Reliquary is free software under the [GNU Affero General Public License v3.0](LICENSE).
Card data comes from [Scryfall](https://scryfall.com) and [MTGJSON](https://mtgjson.com), under their terms.
Card images stay on the Scryfall servers.

## Legal notice

Reliquary is unofficial Fan Content permitted under the Fan Content Policy.
Not approved/endorsed by Wizards.
Portions of the materials used are property of Wizards of the Coast.
©Wizards of the Coast LLC.
