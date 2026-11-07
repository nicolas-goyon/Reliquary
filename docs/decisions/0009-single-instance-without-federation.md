# 0009. One instance without federation

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

A federation connects several instances of an application, so that the instances exchange data.
The project had to choose between a federation of Reliquary instances and one instance that scales.
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
- The scaling of an instance uses the replicas of the API, the separate workers and the split of the database.
  The [technology stack](../architecture/tech-stack.md) gives the details.
- Each user can still export all their data at any time, in open formats.
