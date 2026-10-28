# 0035. Moderation of the public content in the Decks component

- Status: Accepted
- Date: 2026-10-28
- Deciders: @nicolas-goyon

## Context

Users publish decks and have an author profile ([ADR 0029](0029-public-deck-read-model.md)).
A public instance must let the users report bad content, and let the administrators remove it from the public pages.
Issue [#160](https://github.com/nicolas-goyon/reliquary/issues/160) asks for reports of public decks and author profiles, with a reason, a comment and a rate limit.
Issue [#161](https://github.com/nicolas-goyon/reliquary/issues/161) asks for a moderation page, hidden decks and profiles, and a moderation log.
A hidden deck must leave the search of public decks in less than 10 seconds.

These facts limit the options:

- A core component never references another core component ([ADR 0007](0007-modular-monolith.md)).
- The decks are in the shard of their owner. The read model of the public decks, the favorites and the follows are in the users database ([ADR 0029](0029-public-deck-read-model.md)).
- The Decks component owns the public pages: the search, `GET /decks/{id}` and `GET /authors/{id}`.
  The Accounts component gives only the names of the users, through `IAccountDirectory`.
- The export and the deletion of an account must include all the data of the account ([ADR 0032](0032-personal-data.md)).

We looked at three options for the owner of the moderation:

1. A new core component. It needs a new project and interfaces to hide the content of the Decks component. The architecture rules do not permit it to change the read model of Decks.
2. The Accounts component for the profiles and the Decks component for the decks. The profile page is in Decks, so Accounts cannot hide it without a new interface. The administrators then use two lists.
3. A folder `Moderation` in the Decks component for the decks and the profiles. The component changes its own read model and its own endpoints, with no new interface.

## Decision

- The Decks component owns the moderation, in the folder `src/Components/Reliquary.Decks/Moderation` (option 3).
  The items are the public decks and the author profiles.
- The data is in the users database, because the reports and the hidden items are global and not data of one shard:
  - `users.moderation_reports`: the reports. A partial unique index permits one open report for each user and item.
  - `users.moderation_hidden`: the hidden decks and the hidden author profiles.
  - `users.moderation_log`: the actions of the administrators: who, what, when and the reason.
  - Each row has the owner of the item in `item_owner_id`, so the deletion of an account finds all its rows.
- A report has a reason code (`spam`, `offensive`, `copyright` or `other`) and a comment of 500 characters or fewer.
  A user can report a public deck or an author profile that is not hidden, and cannot report their own content.
  A user sends 20 reports in 24 hours at most (`Moderation:ReportsPerDay`). The API counts the reports in the table, so all the API replicas share the limit.
- An administrator (`AuthPolicies.Administrator`) lists the reports, resolves or dismisses a report, hides an item and shows it again.
  When an administrator hides an item, its open reports become resolved.
- When an administrator hides an item, the same transaction removes the rows of the item from `users.public_decks`.
  So the search does not show the deck after the response, and the outbox has no part in the delay.
  The read model consumer does not write a row for a hidden deck or for a deck of a hidden author.
  When an administrator shows the item again, the API writes the rows again in the same request.
- A hidden deck gives the status 404 to all the users except the owner, also with its link.
  The owner sees the deck with `hidden: true` and a notice on the deck page.
- A hidden author profile gives the status 404 on `GET /authors/{id}`, except for the author. Its decks leave the search.
  The links of its unlisted and public decks continue to work. To block a link, hide the deck.
- The Decks component writes the domain events `decks.content.hidden` and `decks.content.shown` to `users.outbox`, so the modules can follow the moderation.
- Personal data ([ADR 0032](0032-personal-data.md)): the provider `moderation` exports three lists of the user.
  The lists are the reports of the user, the hidden items of the user and the actions of the user as an administrator.
  At the deletion of an account, the provider deletes the reports of the user and the reports about the content of the user.
  It also deletes the hidden items of the user.
  The moderation log keeps its entries, because the instance needs the history of the actions.
  The deletion removes the account ID, the item ID and the item name from these entries.

## Consequences

- The moderation needs no new interface in the building blocks and no new project.
  If a future component has public content, for example shared collections, the moderation must move to the building blocks or to a new component.
- The page `/admin/moderation` of the web app shows the reports, the hidden items and the log to the administrators only.
  The web app uses the field `isAdministrator` of the account. The API checks the role on each request.
- A report keeps the name of the deck at the time of the report. So the administrators see the name of a hidden deck.
- `GET /decks/{id}` reads the users database one more time, to find out if the deck is hidden.
- An administrator cannot open a hidden deck of another user. The administrator reads the deck before the action, or shows it again.
- The tests that look for the owner ID after a deletion also look in the moderation tables. They pass, because the deletion removes the IDs from the log.
