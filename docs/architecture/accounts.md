# Accounts and authentication

This page tells how the Accounts component signs in the users and protects the public API.
Terms follow the [glossary](../glossary.md).
[ADR 0022](../decisions/0022-authentication-and-api-keys.md) gives the reasons for the design.

## Credentials

The public API accepts two credentials in the header `Authorization: Bearer`:

| Credential | Format | Use |
| --- | --- | --- |
| Access token | A JSON Web Token | The web interface, during a web session |
| API key | `rlq_<prefix>_<secret>` | Scripts and third-party clients |
| Delegation token | `rld_<prefix>_<secret>` | A module that acts for the user, with some scopes |

The secret of a service account (`rls_<prefix>_<secret>`) works only on the internal API.
The [service accounts](service-accounts.md) page describes the service accounts, the delegations and the scopes.

A request with no credential can use the public endpoints, for example the card endpoints.
A request with a credential that is not valid gets the error `invalid_authentication`, also on a public endpoint.

## Web sessions

1. The user signs in with `POST /api/v1/auth/sign-in`, or with an OAuth provider.
2. The API returns an access token in the body and writes the refresh token in the cookie `reliquary_refresh`.
3. The web interface keeps the access token in memory and sends it with each request.
4. Before the access token expires, or after an error `invalid_authentication`, the web interface calls `POST /api/v1/auth/refresh`.
   The API replaces the cookie and returns a new access token.
5. `POST /api/v1/auth/sign-out` revokes the session and deletes the cookie.

When the page opens again, the web interface calls `POST /api/v1/auth/refresh` to get an access token from the cookie.

The access token contains these claims:

| Claim | Meaning |
| --- | --- |
| `sub` | The ID of the account. It is also the ID of the owner of the user data. |
| `name` | The display name. |
| `lang` | The interface language: `en` or `fr`. |
| `amr` | The authentication method: `session`, `api_key`, `delegation` or `service_account`. |
| `role` | `administrator` for an administrator of the instance. |

## Endpoints

| Endpoint | Credential | Effect |
| --- | --- | --- |
| `POST /auth/register` | — | Makes an account and sends the email of the address check. |
| `POST /auth/email-check` | — | Marks the address as checked, with the token of the email. |
| `POST /auth/email-check/resend` | — | Sends the email of the address check again. |
| `POST /auth/sign-in` | — | Starts a web session with a password. |
| `POST /auth/refresh` | Refresh cookie | Gives a new access token. |
| `POST /auth/sign-out` | Refresh cookie | Stops the web session. |
| `POST /auth/password/forgot` | — | Sends the email of the password reset. |
| `POST /auth/password/reset` | — | Sets a new password with the token of the email. |
| `GET /auth/providers` | — | Lists the enabled OAuth providers. |
| `GET /auth/external/{provider}` | — | Sends the browser to an OAuth provider. |
| `GET /me` | Access token, API key or delegation token with `profile:read` | Gets the account. |
| `PATCH /me` | Access token | Changes the display name or the language. |
| `POST /me/password` | Access token | Changes the password and starts a new session. |
| `GET /me/api-keys` | Access token | Lists the API keys. |
| `POST /me/api-keys` | Access token | Creates an API key. |
| `DELETE /me/api-keys/{id}` | Access token | Revokes an API key. |
| `GET /service-accounts` | Access token | Lists the service accounts that can receive a delegation. |
| `GET /me/delegations` | Access token | Lists the active delegations. |
| `POST /me/delegations` | Access token | Gives a delegation to a service account. |
| `DELETE /me/delegations/{id}` | Access token | Revokes a delegation. |

All the paths start with `/api/v1`.
The OpenAPI document at `/openapi/v1.json` gives the bodies and the responses.

## Sign-in with an OAuth provider

1. The web interface opens `GET /api/v1/auth/external/{provider}?returnUrl=/decks&language=fr` in the browser.
2. The API sends the browser to the provider. The provider sends the browser back to `/api/v1/auth/external/{provider}/callback`.
3. The API finds the account:
   - If the provider account has an OAuth login, the API uses its account.
   - Else, if an account has the same email address and the provider checked the address, the API adds the OAuth login to that account.
   - Else, the API makes a new account with a checked address and no password.
4. The API writes the refresh cookie and sends the browser to `/auth/complete?returnUrl=...` on the web interface.
   If the sign-in fails, the address also has a parameter `error`: `external_sign_in_failed`, `external_email_missing` or `external_email_not_verified`.
5. The page `/auth/complete` calls `POST /api/v1/auth/refresh`.

Register the callback address of each provider in its developer console, for example `https://reliquary.example.org/api/v1/auth/external/github/callback`.

## Authorization in the core components

The other core components do not reference the Accounts component.
They use the contracts of `Reliquary.BuildingBlocks.Security`:

- `RequireAuthorization(AuthPolicies.User)` accepts an access token, an API key or a delegation token. It refuses a service account.
- `RequireAuthorization(AuthPolicies.Session)` accepts only an access token.
- `RequireAuthorization(AuthPolicies.Administrator)` accepts only the access token of an administrator.
- `RequireAuthorization(AuthPolicies.ServiceAccount)` accepts only the secret of a service account. The internal API uses it.
- `RequireScope(Scopes.DecksRead)` lets a delegation token with this scope use the endpoint. An endpoint without a scope refuses the delegation tokens.
- `user.GetAccountId()` gives the ID of the account, which is also the owner ID.

The domain event `accounts.account.created` tells the other core components about a new owner.
The registration assigns the new owner to a shard in the same transaction.

## Quotas

The quotas apply to the requests with an API key, on all the endpoints of the public API.
The requests of a web session have no quota.

Each response to a request with an API key has these headers:

| Header | Meaning |
| --- | --- |
| `RateLimit-Limit` | The daily limit that applies. |
| `RateLimit-Remaining` | The number of requests that remain today. |
| `RateLimit-Reset` | The number of seconds until 00:00 UTC. |

When the key or the account has no remaining request, the API returns the error `quota_exceeded` with a `Retry-After` header.
The scheduled job `accounts.usage-flush` copies the counters of Redis to the table `users.usage_counters` every 5 minutes.

## Emails

The API sends emails for the address check, the password reset, and a second registration with a known address.
The texts are in `src/Components/Reliquary.Accounts/Emails`, in English and in French.
The API uses the language of the account.

With no SMTP server in the settings, the API writes each email to the log.
The local infrastructure has Mailpit: open `http://localhost:18025` to read the emails.

## Personal data

[ADR 0032](../decisions/0032-personal-data.md) describes the export and the deletion of the data of an account.

| Endpoint | Effect |
| --- | --- |
| `GET /me/exports` | Lists the recent exports of the account. |
| `POST /me/exports` | Asks for a ZIP archive of all the data. The worker makes it in one minute or less. |
| `GET /me/exports/{id}/archive` | Downloads the archive. It stays available for 7 days. |
| `GET /me/deletion` | Gets the deletion request. |
| `POST /me/deletion` | Asks for the deletion of the account, with the password. The worker deletes the account and all its data after 7 days. |
| `DELETE /me/deletion` | Cancels the deletion request. |

## Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| `Auth:SigningKey` | — | The key that signs the access tokens: 32 random bytes or more, in base64. Necessary. Use the same key on all the replicas. |
| `Auth:PublicUrl` | `http://localhost:5173` | The address of the web interface, for the links in the emails and the end of an OAuth sign-in. |
| `Auth:AccessTokenLifetime` | `00:15:00` | The lifetime of an access token. |
| `Auth:RefreshTokenLifetime` | `30.00:00:00` | The maximum time without a visit before the user must sign in again. |
| `Auth:RequireCheckedEmail` | `true` | A new account with a password must check its address before the first sign-in. |
| `Auth:EmailCheckLifetime` | `2.00:00:00` | The lifetime of the link of the address check. |
| `Auth:PasswordResetLifetime` | `01:00:00` | The lifetime of the link of the password reset. |
| `Auth:AdministratorEmails` | — | The email addresses of the administrators. |
| `Auth:RequestsPerMinute` | `10` | The rate limit of the authentication endpoints, for each client address and replica. |
| `Auth:DelegationTokenLifetime` | `01:00:00` | The lifetime of a delegation token. A token never stays valid after the end of its delegation. |
| `Auth:Providers:Google:ClientId` and `ClientSecret` | — | Enables the sign-in with Google. The same settings exist for `GitHub` and `Discord`. |
| `Quotas:RequestsPerKeyPerDay` | `10000` | The daily quota of each API key. |
| `Quotas:RequestsPerAccountPerDay` | `10000` | The daily quota of all the API keys of one account. |
| `Email:From` | — | The sender address of the emails. |
| `Email:Smtp:Host`, `Port`, `UserName`, `Password`, `Security` | — | The SMTP server. With no host, the API writes the emails to the log. |

To make a signing key:

```shell
openssl rand -base64 32
```
