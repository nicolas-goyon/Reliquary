# 0022. Authentication, web sessions and API keys

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

The specification asks for accounts with a sign-in by email and by OAuth.
It also asks for personal API keys, with a daily quota for each key and each account.
The API must be stateless: any replica can serve any request ([ADR 0007](0007-modular-monolith.md)).
The web interface is a single-page app that calls the public API.

The options for the web sessions:

1. A cookie session on the server. It is simple, but each replica must read the session store for each request, and the scripts of plugins cannot use the session.
2. A long access token in the local storage of the browser. A script that reads the storage can steal the token for a long time.
3. A short access token in memory, and a refresh token in an HttpOnly cookie. The replicas check the access token with no database access. A script cannot read the refresh token.

The options for the identity code: ASP.NET Core Identity, or a small Accounts component on the platform of [ADR 0017](0017-data-access-and-migrations.md).
Identity uses Entity Framework Core, and the project uses Npgsql and Dapper.

## Decision

- The Accounts component owns the tables of the accounts in the `users` schema. It uses the password hasher of ASP.NET Core Identity (PBKDF2), but not the rest of Identity.
- A web session has an access token and a refresh token:
  - The access token is a JSON Web Token, signed with HMAC SHA-256 and the setting `Auth:SigningKey`. It is valid for 15 minutes.
    The web interface keeps it in memory and sends it in the header `Authorization: Bearer`.
  - The refresh token is a random secret in the cookie `reliquary_refresh`: HttpOnly, Secure, SameSite=Strict, with the path `/api/v1/auth`.
    The database stores only its SHA-256 hash. Each refresh replaces it.
  - When a used refresh token comes back more than 30 seconds after its use, the API revokes all the tokens of the session.
    In the first 30 seconds, the API gives an access token and keeps the cookie, because two tabs can refresh at the same time.
  - A change of the password or a password reset changes the security stamp of the account. This revokes all the sessions.
- A new account with a password must check its email address before the first sign-in. The setting `Auth:RequireCheckedEmail` can disable this.
- The registration, the forgotten password and the email check give the same response for a known and an unknown address.
  Thus they do not tell which addresses have an account.
- The sign-in with Google, GitHub or Discord uses the OAuth handlers of ASP.NET Core. The instance enables a provider when its settings have a client ID and a client secret.
  The API links a provider account to an existing account only when the provider checked the email address.
- A personal API key has the format `rlq_<prefix>_<secret>`. The database stores the prefix and a hash of the secret. The full key is visible one time only.
  An API key cannot manage the account, the password or the API keys. These operations need a web session.
- An endpoint filter on all the endpoints of the public API counts the requests of the API keys in Redis, with one Lua script.
  Each key and each account has a daily quota (UTC). The default is 10,000 requests.
  The worker copies the counters to the table `users.usage_counters` every 5 minutes.
- The authentication endpoints have a rate limit for each client address. Each replica has its own limit.
- The data protection keys are in Redis, so all the replicas can read the cookies and the OAuth states of the other replicas.
- The setting `Auth:AdministratorEmails` gives the administrators of the instance.

## Consequences

- The replicas check an access token with no database access. A revoked session can continue for 15 minutes at most.
- The API and the web interface must be on the same site, so the browser sends the SameSite=Strict cookie. The Vite server and the Nginx server of the web image send `/api` to the API.
- All the replicas must have the same `Auth:SigningKey`.
- The other core components read the user from the claims, with `ClaimsPrincipalExtensions.GetAccountId`, and protect their endpoints with the policies of `AuthPolicies`.
- When Redis is not available, the API does not count the requests of the API keys. It writes a warning to the log.
- The change of the email address, the deletion of an account, the service accounts and the delegation tokens are later work.
