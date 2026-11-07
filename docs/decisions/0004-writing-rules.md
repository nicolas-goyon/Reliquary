# 0004. Writing rules: ASD-STE100 Simplified Technical English

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

Many readers of the project do not read English as their first language.
Other readers are translation tools and AI agents.
Ambiguous text causes wrong implementations and wrong translations.
ASD-STE100 is a controlled language from the aerospace industry that removes ambiguity from technical English.

## Decision

- All English text of the project follows ASD-STE100 Issue 9.
  This includes the documentation, code comments, commit messages, issues and pull requests.
  It also includes the API messages, the log messages and the English interface text.
- The text uses one of two modes. The strict mode applies to procedures, error messages, log messages and instructions. The STE-flavored mode applies to explanations.
- The project [glossary](../glossary.md) gives one meaning to each term.
- CI checks the text with the linter of the asd-ste100 skill (MIT license), through a project configuration in `tools/ste-lint`.
- CI also checks the Markdown format with markdownlint-cli2.
- The repository does not contain the official STE dictionary, because ASD limits its reproduction. The project applies the principle of the dictionary: the plainest common word, with the same meaning every time.

## Consequences

- Some texts are longer, but each sentence has one reading.
- The check uses patterns, not a grammar. It finds the frequent problems. The review of each pull request finds the others.
- Hard findings make the CI fail. Advisory findings, such as passive voice, do not make it fail.
- Contributors and AI agents must read the [writing style guide](../contributing/writing-style.md).
