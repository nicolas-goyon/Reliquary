# 0036. Internal API for the modules

- Status: Accepted
- Date: 2026-10-28
- Deciders: @nicolas-goyon

## Context

[ADR 0007](0007-modular-monolith.md) and [ADR 0010](0010-plugins-and-modules.md) say that a module communicates with the core through a versioned internal API and domain events.
Item 5 of the preparation for modules asks for this internal API, separate from the public API, with an OpenAPI description.
Issue [#162](https://github.com/nicolas-goyon/reliquary/issues/162) asks for a small first version.
It must give the catalog of the domain events and the public profile of a user.
It must also let a module act on the decks and the collection of a user, through a delegation.

The options for the place of the internal API:

1. A path of the API host, `/internal/v1`, with its own OpenAPI document.
2. A separate host process for the internal API.
3. No internal API: the modules use only the public API.

The options to act for a user:

1. Copy the endpoints of the decks and of the collection into the internal API.
2. Use the endpoints of the public API with a delegation token.

## Decision

- The API host serves the internal API under `/internal/v1`.
  The OpenAPI document `internal-v1` at `/openapi/internal-v1.json` describes it. The public OpenAPI document does not contain it.
- Only a service account can use the internal API: the policy `AuthPolicies.ServiceAccount`.
  [ADR 0037](0037-service-accounts-and-delegation-tokens.md) describes the service accounts and the delegation tokens.
- The version of the internal API is the core API version of [ADR 0023](0023-extension-registry.md).
  The major version is the version in the path. `GET /internal/v1` gives the full version.
  This change adds the internal API, so the core API version becomes 1.1.0.
- A core component adds its internal endpoints in `ICoreComponent.MapInternalEndpoints`. The platform adds the endpoints that do not belong to a core component.
- The first endpoints give the version, the catalog of the domain event types and the public profile of a user.
- A module acts for a user with the endpoints of the public API and a delegation token.
  The internal API does not copy these endpoints.
- The API makes the catalog of the domain event types from the `DomainEvent` attributes of the core components.
  The [domain events](../architecture/domain-events.md) page contains the same catalog, and a test compares the two lists.
- The repository contains a copy of the OpenAPI document of the internal API: [internal-api-v1.json](../architecture/internal-api-v1.json).
  A test compares the document of the API host with the copy.

## Consequences

- One implementation serves the web interface, the plugins and the modules for the decks. The validation and the rules stay in one place.
- The scope check of the delegation tokens must cover the endpoints of the public API. ADR 0037 gives the rules.
- A change of the internal API changes the copy of the OpenAPI document in the same pull request. The review can see each change of the contract.
- A new event type without a row in the documentation makes the test fail.
- The internal API and the public API share the replicas of the API host. A separate host stays possible later, because the internal endpoints are in their own route group.
- The Nginx server of the web image sends only `/api` to the API host. A module reaches the internal API on the private network.
- The internal API does not give the domain events themselves yet. A module in the same process uses `IDomainEventConsumer`. An event feed for a separate module service is later work.
- The [internal API](../architecture/internal-api.md) page gives the details.
