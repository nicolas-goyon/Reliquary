# 0019. Three hosts and scheduled jobs in the worker

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

The specification asks for background tasks in separate workers that scale independently of the API: the Scryfall synchronization, auto-tag and the tasks of modules.
It proposes Hangfire or dedicated workers.
The administrator also needs commands, for example to apply the migrations before a deployment.
Several replicas of the worker can run at the same time.

## Decision

- Reliquary has three hosts:
  - `Reliquary.Api`: the public API
  - `Reliquary.Worker`: the outbox publisher, the scheduled jobs and the event consumers
  - `Reliquary.Cli`: the command `reliquary` for the administrators
- The project `Reliquary.Hosting` registers the platform and the core components in the three hosts.
- The project `Reliquary.Platform` contains the shared infrastructure. The core components do not reference it. They use the interfaces of `Reliquary.BuildingBlocks`.
- A core component declares a scheduled job with `IScheduledJob`: a name, an interval and a method.
- The worker runs each job when the interval since the end of its last run is complete. The table `users.job_runs` records each run.
- A PostgreSQL advisory lock stops two worker replicas that run the same job.
- Hangfire is not chosen. The jobs need no dashboard and no queue for now.

## Consequences

- The API and the worker scale independently.
- A restart does not run a job too early, because the table `users.job_runs` keeps the time of the last run.
- A job must accept a stop in the middle of a run, and give a correct result when it runs again.
- If the project needs queues or a dashboard later, a new ADR can replace this decision.
