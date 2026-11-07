# 0005. Git workflow: develop, main and pull requests

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

The maintainer wants a review for each change, also for the changes of the maintainer.
The project needs a stable branch for releases and an integration branch for the daily work.

## Decision

- `main` contains the released versions. `develop` is the integration branch.
- Each change goes through a pull request. Nobody pushes directly to `main` or to `develop`.
- Work branches start from `develop`. Their names follow the pattern `<type>/<short-name>`.
- Pull request titles and commit messages follow Conventional Commits. A CI check reads the title of each pull request.
- Work pull requests merge into `develop` with "Squash and merge".
- Release pull requests merge from `develop` into `main` with "Create a merge commit". A tag then marks the version.
- GitHub rules protect `main` and `develop`: a pull request is necessary, and nobody can force a push or delete the branch.
- At the start the maintainer works alone, so the rules require no approval.

## Consequences

- The history of `develop` has one commit for each pull request. A changelog tool can read the titles.
- The merge commits of the releases keep `main` and `develop` in agreement.
- The settings of the GitHub repository must agree with this decision.
  The settings include the default branch `develop`, the branch rules, the automatic deletion of merged branches and the private vulnerability reports.
- When more maintainers join, the rules can require one approval.
