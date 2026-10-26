# 0009. One instance without federation

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

A federation connects several instances of an application, so that the instances exchange data.
The specification considers a federation between Reliquary instances.
Reliquary must also scale to many users.
The architecture can already scale one instance:

- The API keeps no state, so Kubernetes can add replicas of the API.
- Separate workers run the background tasks.
- The shard directory can split the user data over several databases.

## Decision

- Reliquary has no federation between instances.
- One instance serves all its users.
- Kubernetes replicas scale the instance.

## Consequences

- An instance does not exchange data with other instances.
  For example, the users of one instance cannot see the public decks of another instance.
- The scaling of an instance uses the replicas of the API, the separate workers and the split of the database.
  The [technology stack](../architecture/tech-stack.md) gives the details.
- Each user can still export all their data at any time, in open formats.
