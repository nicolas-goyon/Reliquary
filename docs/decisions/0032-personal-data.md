# 0032. Export and deletion of the personal data

- Status: Accepted
- Date: 2026-10-28
- Deciders: @nicolas-goyon

## Context

A public instance in Europe must let each user get all their data and delete their account (GDPR, articles 17 and 20).
The data of a user is in several core components.
The account and the read model of the public decks are in the users database. The decks are in the shard of the user.
The core components must not read the tables of another core component ([ADR 0007](0007-modular-monolith.md)).
Issue [#112](https://github.com/nicolas-goyon/reliquary/issues/112) asks for an archive in open formats.
Issue [#113](https://github.com/nicolas-goyon/reliquary/issues/113) asks for a deletion with a delay of 7 days, and for no owner ID in the tables after the deletion.

## Decision

- Each core component that stores data of an owner implements the interface `IPersonalDataProvider` of the building blocks.
  The provider writes the data of the owner to the export archive, and it deletes the data of the owner.
- The Accounts component owns the process. The worker runs the scheduled job `accounts.personal_data` every minute.
- **Export.** `POST /me/exports` records a request. The job makes a ZIP archive with one folder for each provider and keeps it in the table `users.data_exports` for 7 days.
  The archive contains JSON files and the decks in the [deck text format](../development/deck-text-format.md). It never contains a password hash, a token or a secret.
  The account page shows a download link when the archive is ready.
- **Deletion.** `POST /me/deletion` with the password records a request. The user can cancel it with `DELETE /me/deletion` for 7 days.
  After 7 days, the job calls the providers in the order of `DeletionOrder`, then deletes the account and its shard assignment in one transaction.
  A failure stops the deletion, and the next run starts it again. Each provider gives the same result when it runs two times.
- The job calls the providers directly and does not wait for a domain event.
  The shard assignment must stay until each provider deleted the data in the shard, and the order of the event consumers is not fixed.
  After the deletion, the job writes the event `accounts.account.deleted`, so the modules delete their own data.
- The Decks component deletes the decks, the tags and the folders of the owner.
  It also deletes the public deck rows, the favorites, the follows and the copy links of the owner.
  The copies by other users keep their cards, but they lose the link to the deleted deck.

## Consequences

- A new core component with data of an owner must implement `IPersonalDataProvider`. A test checks that no table contains the owner ID after a deletion.
- The outbox tables keep the events of the owner until the publisher deletes them, 7 days after their publication (`Outbox:Retention`).
  These events contain IDs only, and no other personal data.
- The archive is in the users database. A very large account makes a large row. The project accepts this for M1.
- The deletion of the last administrator of an instance is not blocked yet.
