# Requirements

This page gives what each mode needs.
Terms follow the [glossary](../glossary.md).

## Software

| Software | Local instance | Pre-production instance | Production instance |
| --- | --- | --- | --- |
| Docker with the Compose plugin 2.17 or later | Yes | Yes | Yes (Docker Compose) |
| A POSIX shell (Linux, macOS, or WSL on Windows) | Yes | Yes | Yes |
| [just](https://just.systems) 1.40 or later | Yes | Optional | No |
| Git | Yes | Optional | No |
| .NET SDK 10.0 (see `global.json`) | Only for hot reload | No | No |
| Node.js 24, or 22.12 and later | Only for hot reload | No | No |
| Python 3.11 or later | Only for the documentation checks | No | No |
| A reverse proxy with HTTPS, for example Caddy | No | No | Yes |

On Windows, run the commands in WSL or in Git Bash. The script `reliquary-update` needs WSL.

## Hardware

| Item | Local instance | Pre-production or production instance |
| --- | --- | --- |
| Processor | 64-bit x86 or ARM | 64-bit x86 or ARM |
| Memory | 2 GB free | 2 GB free, 4 GB during the card import |
| Disk | 2 GB with the demo data | 4 GB for all the cards, plus the backups |

The published images exist for `linux/amd64` and `linux/arm64`.
Each backup contains all the databases. With all the cards, a backup can need 1 GB or more.

## Network

| Address | Local instance | Pre-production instance | Production instance |
| --- | --- | --- | --- |
| Web app and API | <http://localhost:8080> | <http://localhost:8090> | The address of your reverse proxy |
| Mailpit (test emails) | <http://localhost:18025> | <http://localhost:18035> | Not used with a real email server |
| PostgreSQL | `localhost:15432` | Not open | Not open |
| Redis | `localhost:16379` | Not open | Not open |

The `.env` file of each instance can change the ports.

An instance needs these outbound connections:

- `ghcr.io`, to pull the published images (pre-production and production instances).
- `api.scryfall.com`, `data.scryfall.io`, `mtgjson.com` and `json.commanderspellbook.com`, to import the card data.
- The email server, when you use a real email server.
