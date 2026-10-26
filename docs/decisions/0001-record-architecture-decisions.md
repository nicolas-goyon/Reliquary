# 0001. Record architecture decisions

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

Reliquary makes architecture decisions from the start, for example about the license, the languages, the database and the extensions.
Contributors and AI agents must know the reason for each decision before they change the code.
Without the reason, a contributor can accept a decision with no understanding, or change it by accident.
The first specification of the project is in French, and it gives the reasons in different places.

## Decision

- The project records each architecture decision in an architecture decision record (ADR).
- The ADRs use the format of Michael Nygard, from the article [Documenting Architecture Decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions).
- Each ADR is a Markdown file in `docs/decisions`.
- Each ADR has a number of four digits.
  The file name is the number and a short name, for example `0007-modular-monolith.md`.
- The project gives the numbers in sequence, and never uses a number again.
- Each ADR starts with its number and its title, then gives its status, its date and its deciders.
  Then it has three parts, each with a level-2 heading: Context, Decision and Consequences.
  The [template](template.md) gives the format.
- The status of an ADR is one of these values:
  - **Proposed**: the project discusses the decision.
  - **Accepted**: the decision applies.
  - **Superseded by NNNN**: ADR NNNN replaces the decision.
- When a new decision replaces an accepted decision, a new ADR records the new decision.
  The old ADR stays in the folder, and its status changes to "Superseded by NNNN".
- Each open question is a GitHub issue with the label `type: decision`.
  When the project makes the decision, an ADR records it, and the pull request of the ADR closes the issue.
- A pull request adds or changes an ADR.
- The [index](README.md) of this folder lists all the ADRs.

## Consequences

- The reasons for each decision stay in the repository, next to the code.
- Contributors and AI agents can read the decisions before they change the code.
- Each architecture decision needs a written record and a review.
  This adds work to each decision.
- The history of a decision stays visible, because a superseded ADR stays in the folder.
