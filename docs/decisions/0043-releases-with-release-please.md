# 0043. Releases with release-please

- Status: Accepted
- Date: 2026-11-07
- Deciders: @nicolas-goyon

## Context

[ADR 0005](0005-git-workflow.md) gives two branches: `develop` for the daily work and `main` for the released versions.
A merge commit brings `develop` into `main`, and a tag then marks the version.
Until now, the project had no release and no tag. The version stayed `0.1.0`.

The pre-production instance can use the channel `latest` (issue [#193](https://github.com/nicolas-goyon/reliquary/issues/193)).
This channel needs releases, and each release needs its images.

The titles of the pull requests follow Conventional Commits, and each pull request into `develop` is one commit.
Thus a tool can calculate the next version from the history.
The maintainer selected release-please for this work.

Release-please uses one target branch. The questions are:

- the branch on which release-please runs
- the return of the version change to `develop`
- the file `CHANGELOG.md`, which the contributors write by hand in ASD-STE100 with a section "Unreleased"

## Decision

- Release-please runs on `main`. The steps of a release are:
  1. A pull request merges `develop` into `main` with "Create a merge commit". The recipe `just release-start` opens it.
  2. Release-please reads the commits since the last tag. It opens or updates the release pull request "chore(release): release X.Y.Z" into `main`.
  3. The maintainer merges the release pull request.
     Release-please then creates the tag `vX.Y.Z` and the GitHub release.
  4. The release workflow calls the image workflow with the tag, because a tag that a workflow creates does not start other workflows.
     The images get the tags `X.Y.Z`, `X.Y` and `latest`.
  5. The release workflow opens a pull request that merges `main` into `develop`. The maintainer merges it with "Create a merge commit".
- The version rule: before `1.0.0`, a change of type `feat` increases the minor number, and a change of type `fix` increases the patch number.
  A breaking change increases the minor number, not the major number.
- Release-please writes the version to `version.txt`, `.release-please-manifest.json`, `Directory.Build.props` and `web/package.json`.
- Release-please does not write the changelog.
  The contributors continue to write the notable changes in the section "Unreleased" of `CHANGELOG.md`.
  In the release pull request, the script `tools/release/changelog.py` adds the heading of the version below the heading "Unreleased".
  Thus the notes of "Unreleased" become the notes of the release, and the section "Unreleased" becomes empty.
- The first release is `0.2.0`. It contains the work of the milestones M0 to M2 and the work after M2.

## Consequences

- A release needs two merges in the GitHub interface, and one merge to bring the version back to `develop`.
- The changelog stays in ASD-STE100. The titles of the pull requests do not replace it.
- GitHub does not start the workflows on a pull request that the token of a workflow opens.
  Thus the release pull request and the pull request back to `develop` have no CI checks. The commits of `develop` already passed the checks.
- The setting "Allow GitHub Actions to create and approve pull requests" of the repository must be on. Otherwise release-please cannot open its pull request.
- After a release, `develop` has the version of the last release, for example `0.2.0`.
  The images of `develop` then have the version `0.2.1-develop.N`, where N is the number of the workflow run.
  In Semantic Versioning, this version comes after `0.2.0` and before the next release.
