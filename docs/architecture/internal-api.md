# Internal API

This file describes the internal API: the versioned API between the modules and the core.
Terms follow the [glossary](../glossary.md).
[ADR 0036](../decisions/0036-internal-api.md) gives the reasons for the design.

## Rules

- The internal API is separate from the public API.
  Its endpoints are under `/internal/v1`, and the public endpoints are under `/api/v1`.
- Only a service account can use the internal API.
  A request with no credential gets the status 401.
  A request with the access token or the API key of a user gets the status 403.
- The API host serves the internal API. A module calls the API host on the private network of the instance.
  The Nginx server of the web image sends only `/api` to the API host.
  Thus the public address of the instance does not give the internal API.
- The internal API follows the [API conventions](api-conventions.md): JSON bodies, problem details and error codes.

## Versions

The version of the internal API is the [core API version](../decisions/0023-extension-registry.md), for example `1.1.0`.

- The major version is the version in the path: `/internal/v1` for the versions `1.x.y`.
- A compatible addition increases the minor version, for example a new endpoint or a new optional field.
- A change that can break a module increases the major version and gives a new path, for example `/internal/v2`.
- `GET /internal/v1` gives the current version. A module checks it before it uses the internal API.
- The manifest of a module gives the supported versions in the field `coreApi`, for example `^1.1`.

## OpenAPI document

The API host serves the OpenAPI document of the internal API at `/openapi/internal-v1.json`.
The document needs no credential, because it contains no data.
The public OpenAPI document at `/openapi/v1.json` does not contain the internal endpoints.

The file [internal-api-v1.json](internal-api-v1.json) is a copy of the document.
A test compares the document of the API host with this file, so each change of the internal API is visible in the pull request.
When you change the internal API on purpose:

1. Run the tests with the variable `RELIQUARY_UPDATE_SNAPSHOTS=1`. The test writes the new document to the file.
2. Read the difference in Git.
3. Increase `CoreApi.Version` when a module can see the change.
4. Commit the file with the change.

```shell
RELIQUARY_UPDATE_SNAPSHOTS=1 dotnet test tests/Reliquary.Api.Tests --filter InternalOpenApiDocumentMatchesTheSnapshot
```

## Endpoints

| Endpoint | Effect |
| --- | --- |
| `GET /internal/v1` | Gets the version of the core API. |
| `GET /internal/v1/events/types` | Lists the domain event types and versions that modules can consume, with the fields of each payload. |
| `GET /internal/v1/users/{accountId}/profile` | Gets the public profile of a user: the ID and the display name. |

The [domain events](domain-events.md#catalog-of-the-event-types) page gives the same catalog of the event types.

## Add an endpoint

1. Map the endpoint in `MapInternalEndpoints` of the core component, or in `InternalApiEndpoints` of the platform.
   The host adds the path `/internal/v1`, the OpenAPI document and the policy of the service accounts.
2. Give the endpoint a name, a tag and a summary, as for the public API.
3. Update the snapshot of the OpenAPI document, and add a line to the table of this page.
