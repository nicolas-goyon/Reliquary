# 0037. Service accounts and delegation tokens

- Status: Accepted
- Date: 2026-10-28
- Deciders: @nicolas-goyon

## Context

Item 6 of the preparation for modules asks for a model of delegation tokens in the `users` schema.
A delegation token means "act for user X, with this scope", and the user can revoke it.
Issue [#163](https://github.com/nicolas-goyon/reliquary/issues/163) asks for service accounts, delegation tokens with scopes and an expiry, and a check of the scope on each request.
A request with a revoked or expired delegation token must fail with the status 401.
The [internal API](0036-internal-api.md) accepts only service accounts, and a module acts for a user through the public API.

The options for the consent of the user:

1. An OAuth 2 flow: the module sends the browser of the user to a consent page, then the API gives a code to the module.
2. The user creates the delegation for a named service account on the account page. The module then asks the internal API for a token.

The options for the delegation token:

1. A JSON Web Token, signed like the access tokens. The API checks it with no database access, so a revocation takes effect only at the end of the token.
2. A random secret with a hash in the database, like the API keys of [ADR 0022](0022-authentication-and-api-keys.md). The API reads the database for each request, so a revocation takes effect immediately.

The options for the scope check:

1. A check in each endpoint.
2. Metadata on each endpoint and one endpoint filter for all the endpoints of the public API.

## Decision

- **Service accounts.** The table `users.service_accounts` contains the service accounts.
  The administrator creates, lists and revokes them with the CLI command `reliquary service-accounts` or with the endpoints `/api/v1/admin/service-accounts`.
  The secret has the format `rls_<prefix>_<secret>`. The database stores the prefix and a SHA-256 hash of the secret, and the API shows the secret one time only.
  A service account is not a user: its principal has no account ID, so the policy `AuthPolicies.User` refuses it.
- **Delegations.** The table `users.delegations` contains the consent of the users.
  The user gives a delegation to a named service account on the account page, with some scopes and a duration of 1 to 365 days.
  The user sees and revokes the delegations on the same page. Only a web session can manage the delegations. M2 has no OAuth flow.
- **Delegation tokens.** The service account lists its delegations and asks for a token with `POST /internal/v1/delegations/{id}/tokens`.
  The token has the format `rld_<prefix>_<secret>`, and the table `users.delegation_tokens` keeps the prefix and a hash.
  A token is valid for one hour (`Auth:DelegationTokenLifetime`) and never after the end of its delegation.
  The scopes of a token are the scopes of the delegation, or fewer.
- **Check of a request.** The API reads the token, its delegation and its service account in the database for each request.
  A token that expired, a revoked delegation, an expired delegation and a revoked service account give the status 401.
- **Scopes.** The scopes are `decks:read`, `decks:write`, `collection:read`, `collection:write` and `profile:read`.
  An endpoint declares its scope with `RequireScope` of the building blocks. The endpoint filter `ScopeFilter` of the Accounts component checks each request with a delegation token.
  An endpoint without a scope refuses the delegation tokens with the status 403 and the code `scope_missing`. A token without the scope of the endpoint gets the same error.
  The policies `Session` and `Administrator` refuse the delegation tokens.
- **Personal data.** The export of an account contains its delegations. The deletion of an account deletes its delegations and their tokens.

## Consequences

- The user gives the consent in Reliquary, where they are already signed in. A module cannot ask for a delegation itself. A redirect flow for the consent stays possible later.
- Each request with a delegation token reads the database. The project accepts this cost, because the revocation must take effect immediately.
- A new endpoint refuses the delegation tokens by default. Its author adds `RequireScope` when a module needs it.
- The scopes of the collection exist before the endpoints of the collection. Milestone M2 adds `RequireScope` to these endpoints.
- The quotas of the API keys do not apply to the delegation tokens yet.
- The secret of a service account does not change. To replace it, the administrator creates a new service account and revokes the old one.
  The users must then give their delegations again. A rotation of the secret is later work.
- The [service accounts](../architecture/service-accounts.md) page gives the details.
