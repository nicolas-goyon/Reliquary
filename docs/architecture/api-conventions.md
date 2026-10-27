# API conventions

This file gives the rules of the public API.
The web interface, the plugins and the third-party clients use the same API.
Terms follow the [glossary](../glossary.md).

## Versions

- All the endpoints of the public API are under `/api/v1`.
- A change that can break a client goes to a new version, for example `/api/v2`.
  Examples: a removed field, a renamed field, a new required field, or a different meaning.
- A new optional field or a new endpoint stays in the current version.
- The API serves its OpenAPI document at `/openapi/v1.json`. The code of the endpoints produces it.

## Requests

- Send JSON bodies with the content type `application/json`.
- The names of the fields are in camel case: `displayName`, `nextCursor`.
- The values of an enumeration are strings in camel case: `"private"`, `"unlisted"`.
- Dates and times use ISO 8601 with a time zone, in UTC: `2026-10-26T12:00:00Z`.
- Identifiers are UUIDv7 values in their text form.

## Lists

### Paging

The API uses cursors for paging.
A cursor is an opaque text: clients must not read it or change it.

| Query parameter | Meaning |
| --- | --- |
| `limit` | The maximum number of items, from 1 to 100. The default is 50. |
| `cursor` | The `nextCursor` value of the previous page. Omit it for the first page. |

The response of a list has this form:

```json
{
  "items": [],
  "nextCursor": "eyJuYW1lIjoiU29sIFJpbmcifQ"
}
```

On the last page, `nextCursor` is `null`.

### Sort

- The query parameter `sort` gives the sort field, for example `sort=name`.
- A minus sign before the field gives the descending order, for example `sort=-createdAt`.
- Each endpoint lists its sort fields in the OpenAPI document.

### Filters

- Each filter is a query parameter with the name of its field, for example `format=commander`.
- The card search uses the Scryfall syntax in the parameter `q`. The [card search](card-search.md) file describes it.

## Errors

The API returns each error as problem details ([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)), with the content type `application/problem+json`.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "One or more fields of the request are not valid. Correct the fields in the errors list.",
  "code": "validation_failed",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "errors": [
    { "field": "email", "code": "required", "message": "The email is necessary." }
  ]
}
```

| Member | Meaning |
| --- | --- |
| `code` | The stable error code. Clients use it to find the cause and to translate the message. |
| `detail` | An English message for developers. It follows the strict mode of the [writing rules](../contributing/writing-style.md). |
| `traceId` | The identifier of the trace of the request. Give it to the administrator when you report a problem. |
| `errors` | For the code `validation_failed` only: the error of each field. |

Rules for the error codes:

- A code never changes after its release. A new error gets a new code.
- The web interface translates each code in `web/src/locales`. The `detail` message is not for the users.
- A 500 response never contains the details of the exception.

### Error codes

| Code | Status | Meaning |
| --- | --- | --- |
| `bad_request` | 400 | The syntax, the JSON body or a parameter type is not correct. |
| `validation_failed` | 400 | One or more fields are not valid. The `errors` member gives each field. |
| `authentication_required` | 401 | The request needs an access token or an API key. |
| `forbidden` | 403 | The user cannot do this operation. |
| `not_found` | 404 | The resource does not exist, or the user cannot see it. |
| `method_not_allowed` | 405 | The resource does not accept this HTTP method. |
| `conflict` | 409 | The request conflicts with the current state of the resource. |
| `quota_exceeded` | 429 | The API key or the account used all its requests of the day. |
| `rate_limited` | 429 | The client sent too many requests in a short time. |
| `owner_read_only` | 503 | The data of the user moves to another database. The response has a `Retry-After` header. |
| `service_unavailable` | 503 | A service that the server needs is not available. |
| `internal_error` | 500 | An error on the server. |
| `invalid_authentication` | 401 | The request has an access token or an API key, but it is not valid. This also applies to a public endpoint. |
| `invalid_credentials` | 400 or 401 | The email address or the password is not correct. |
| `email_not_checked` | 403 | The user must open the link of the email before the first sign-in. |
| `invalid_token` | 400 | The link of the email is not valid: the user opened it before, it expired, or it is not complete. |
| `session_expired` | 401 | The refresh cookie is missing, expired or revoked. The user must sign in again. |
| `api_key_limit` | 409 | The account has the maximum number of API keys. |
| `provider_not_enabled` | 404 | The OAuth provider is not enabled on the instance. |
| `core_version_not_supported` | 409 | The extension does not support the current version of the core API. |
| `deck_text_invalid` | 400 | The deck text has lines that the API cannot use. The member `lineErrors` gives the number, the reason code and the text of each line. |
| `deck_version_conflict` | 409 | The deck changed since the client read it. Read the deck again, then repeat the change. |
| `search_syntax_error` | 400 | The card search query has a syntax error. The member `position` gives the index of the character. |
| `search_query_empty` | 400 | The card search query has no search condition. The member `warnings` gives the ignored terms. |
| `search_timeout` | 400 | The card search took more than 5 seconds. |

An endpoint can add a more specific code.
The OpenAPI document of the endpoint lists its specific codes.

### Field error codes

| Code | Meaning |
| --- | --- |
| `required` | The field is necessary. |
| `too_short` | The value is too short. |
| `too_long` | The value is too long. |
| `out_of_range` | The value is outside the permitted range. |
| `invalid_format` | The value does not have the correct format. |
| `not_allowed` | The value is not one of the permitted values. |
| `card_not_found` | No card has this name. |

## Statelessness

The API keeps no session in memory.
Each request carries its authentication, so any replica of the API can serve any request.

## Implementation

- `Reliquary.BuildingBlocks.Api.ApiProblems` creates the error responses with a code.
- `Reliquary.BuildingBlocks.Api.ErrorCodes` lists the common codes.
- `Reliquary.BuildingBlocks.Api.PageRequest`, `Page<T>` and `Cursors` give the paging.
- The API host adds the `code` and the `traceId` members to all problem details, also to the errors of the framework.
