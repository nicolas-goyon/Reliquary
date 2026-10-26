# 0024. Container images and the full local instance

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

[ADR 0016](0016-local-infrastructure.md) gives Docker Compose and `just` for the local instance.
Issue [#10](https://github.com/nicolas-goyon/reliquary/issues/10) asks for one command that creates a full instance: the API, the worker and the web app, with the migrations.
A person who tests Reliquary or writes a plugin must not need the .NET SDK or Node.js.
A developer of the core runs the API and the web app with hot reload, so this developer needs the infrastructure only.

The refresh cookie has the attribute SameSite=Strict ([ADR 0022](0022-authentication-and-api-keys.md)), so the web app and the API must be on the same site.

## Decision

- One multi-stage `Dockerfile` at the root builds the images of the .NET hosts. Its targets are `api`, `worker` and `cli`. The images run as a user that is not root.
- `web/Dockerfile` builds the web app and serves it with an unprivileged Nginx image. Nginx sends the `/api` requests to the API.
- The Compose services of the application have the profile `app`, so the infrastructure can start alone.
- `just up` builds the images, starts the infrastructure and applies the migrations with the CLI.
  Then it loads the demo data with `reliquary demo load`, and starts the API, the worker and the web app.
- `just infra` starts the infrastructure only, for development with `just api` and `just web`.
- The images have the tag `reliquary/<host>:local`, so all the local instances share them.
- The instance imports the cards of `data/demo` by default. The setting `RELIQUARY_CARD_DATA` gives another folder.
- A CI job creates two instances, checks them and destroys them.

## Consequences

- A person with Docker and `just` gets a working instance with one command.
- The first `just up` takes a few minutes, because it builds the images. The next commands use the image cache.
- The demo accounts have public passwords. The documentation tells the users to keep a local instance private.
- The production deployment can use the same images, with its own settings and its own reverse proxy.
