# STE linter

This folder contains the check for the ASD-STE100 writing rules.
The [writing style guide](../../docs/contributing/writing-style.md) gives the rules.

| File | Content |
| --- | --- |
| `ste-lint.py` | The linter. It is a copy of `scripts/ste-lint.py` from the [asd-ste100 skill](https://github.com/danyuchn/asd-ste100-skill) by Dustin Yuchen Teng. This project does not change the file. |
| `LICENSE` | The MIT license of `ste-lint.py`. |
| `lint-docs.py` | The project configuration: the files to check and the rules of the glossary. |

## Run the check

Run the check on all project text:

```shell
python3 tools/ste-lint/lint-docs.py
```

Run the check on some files:

```shell
python3 tools/ste-lint/lint-docs.py docs/glossary.md README.md
```

The `just lint-docs` command runs this check and the Markdown check.

## What the check reads

- All Markdown files that Git tracks
- The English interface texts in `web/src/locales/en`

## Project configuration

The glossary gives two different meanings to `remove` and `delete`.
The upstream linter treats the two words as synonyms, so `lint-docs.py` takes `remove` out of that synonym group.
Put each new project rule in `lint-docs.py`, never in `ste-lint.py`.

## Update the linter

1. Get the new version of `scripts/ste-lint.py` from the upstream repository.
2. Replace `ste-lint.py` with the new version. Do not change it.
3. Replace `LICENSE` if the upstream license changed.
4. Run `python3 tools/ste-lint/ste-lint.py --selftest`.
5. Run `just lint-docs`, then correct the new findings.
