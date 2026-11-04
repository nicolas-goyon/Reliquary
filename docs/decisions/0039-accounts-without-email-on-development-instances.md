# 0039. Accounts without email on development instances

- Status: Accepted
- Date: 2026-11-04
- Deciders: @nicolas-goyon

## Context

Each test of a local instance needs a new account. Each new account needs an email address, and the user must open a link in Mailpit.
The column `users.accounts.email` is mandatory. Other parts of the account code use it, for example the administrator check.

## Decision

- The setting `Auth:AllowAccountsWithoutEmail` is `false` by default. The development settings and Docker Compose set it to `true`.
- When the setting is `true`, a request to `POST /auth/register` with no address makes an account with the public name and the password.
- The API stores an internal address: a hash of the normalized public name, in the domain `no-email.reliquary.invalid`. This keeps the database schema, and the unique index makes the public name unique for these accounts.
- `POST /auth/sign-in` accepts the public name in the field `email`. A value with no character `@` is a public name.
- `GET /auth/providers` tells the web interface if the instance enables this method.

## Consequences

- An account with no address cannot reset its password.
- The sign-in uses the public name of the registration. If the user changes the public name later, the sign-in continues to use the first name.
- Before a public release, we must decide if accounts need an email address. The internal addresses do not replace a real design for accounts with no address.
