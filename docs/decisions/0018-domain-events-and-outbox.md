# 0018. Domain events through an outbox in each database

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

[ADR 0007](0007-modular-monolith.md) asks the core components to publish versioned domain events from milestone M0.
Later, modules use these events, also in separate services.
An event must go out only when its data change commits.
The instance can have several databases: the users database, the reference database and the shard databases.
A transaction cannot cover two databases.

## Decision

- Each database has an outbox table: `users.outbox`, `reference.outbox` and `content.outbox`.
- A core component writes its event to the outbox of its database, in the transaction of the data change, with `IDomainEventWriter`.
- The outbox publisher runs in the worker process. It reads the outbox of each database and sends the new events to the consumers (`IDomainEventConsumer`).
- The publisher sends each event at least one time. Consumers must accept an event two times.
- A row lock stops two worker replicas that send the same event.
- After a failure, the publisher waits before it tries again: 2 seconds, then 4, 8 and more, with one hour at most. After 10 failures, it stops.
- The publisher deletes the published events after 7 days.
- Each event type has a name and a version, for example `accounts.account.created`, version 1.

## Consequences

- An event never goes out for a change that the database rolled back.
- The order of the events is the order of their identifiers in each database, but a failure can change the order.
  Consumers must not depend on the order between two databases.
- The worker must run, so that the events go out.
- Later, a module service can read the events through the internal API or a message broker. The outbox stays the source.
- The [domain events](../architecture/domain-events.md) page gives the rules for event names and versions.
