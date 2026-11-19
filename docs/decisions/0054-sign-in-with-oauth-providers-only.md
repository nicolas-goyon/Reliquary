# 0054. Sign-in with OAuth providers only

- Status: Accepted
- Date: 2026-11-18
- Deciders: @nicolas-goyon

## Context

[ADR 0022](0022-authentication-and-api-keys.md) gave two ways to sign in: an email address with a password, and an OAuth provider.
The password way needed much code that has no relation to decks and cards:

- the registration, the check of the email address, the forgotten password and the change of the password,
- a password hasher, single-use tokens and their table,
- an email sender with an SMTP client, email texts in two languages, and a test email server in each local instance,
- rules to hide whether an email address has an account.

Each of these parts is a risk for the security of the accounts, and the project has few maintainers.
An OAuth provider already does this work, and it gives a checked email address.
The specification asks for a sign-in by email and by OAuth. The maintainer decided on 2026-11-18 to keep only OAuth.

## Decision

- A user signs in only with an OAuth provider: Google, GitHub or Discord. An instance enables a provider with its client ID and its client secret.
- The API has no password endpoints: no registration, no email check, no forgotten password, no password change and no sign-in with a password.
  The first sign-in with a provider makes the account.
- An account has no password. The migration `users/0004_sign_in_with_providers_only.sql` drops the columns `password_hash` and `email_checked_at` and the table `account_tokens`.
- Reliquary sends no email. This change removes the contract `IEmailSender`, the SMTP sender, the package MailKit, the email texts and the Mailpit service.
  The account page shows the state of an export and of an account deletion.
- The request to delete the account needs no password. The web session, a confirmation in the web app and the delay of 7 days protect the account.
- The rules of ADR 0022 for the web sessions, the API keys, the OAuth sign-in and the administrators do not change.
  The API keys, the service accounts and the delegation tokens stay ([ADR 0037](0037-service-accounts-and-delegation-tokens.md)).
- **Development sign-in**: the setting `Auth:AllowDevelopmentSignIn` is `false` by default.
  When it is `true`, `POST /api/v1/auth/development/sign-in` with an email address starts a web session for the account of this address.
  The API makes the account when it does not exist.
  - The development settings, the local Docker Compose file and the tests set it to `true`. A local instance and the CI cannot use an OAuth provider.
  - The demo accounts have no password. The sign-in page of a development instance has one button for each demo account.
  - Other instances return the status 404. The API writes a warning to the log for each development sign-in.
  - `GET /api/v1/auth/providers` gives the field `developmentSignIn`, so the web app shows the form only on these instances.
- The existing accounts are not moved. An account that had only a password has no way to sign in.
  When the user signs in with a provider that gives the same checked email address, the API links the provider to this account, as before.
- This ADR replaces [ADR 0039](0039-accounts-without-email-on-development-instances.md). The development sign-in replaces the accounts with no email address.

## Consequences

- The backend loses about 800 lines of code, one table, two columns and one package. The web app loses three pages and one panel.
- A deployed instance needs the OAuth client of one provider or more. Without a provider, no user can sign in, and the sign-in page says so.
- A user without an account at Google, GitHub or Discord cannot use an instance.
- An instance depends on its providers: when a provider is not available, its users cannot start a new session. The sessions that exist continue to work.
- An instance with `Auth:AllowDevelopmentSignIn` gives all its accounts to all the persons who can reach it. The deployment guides tell to keep it `false`.
- The Compose file of the deployed instances keeps a service with the name `mailpit` that does nothing.
  The script `reliquary-update` of the release 0.4.0 and of the older releases starts this service by name.
- The change of the email address of an account stays later work. The address comes from the provider at the first sign-in.

## Alternatives considered

- **Google only.** One provider is the smallest design.
  But each user then needs a Google account. The code of the two other providers is small, so they stay.
- **Keep the password for development instances only.** This keeps most of the password code and its risks for a need of the tests only.
- **A fake OAuth provider for the tests.** The tests would be near the real flow.
  But each local instance and each test would need one more service.
