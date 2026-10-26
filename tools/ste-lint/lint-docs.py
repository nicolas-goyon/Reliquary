#!/usr/bin/env python3
"""Check Reliquary text against the structural ASD-STE100 rules.

The rules come from ste-lint.py. That file is a copy of an MIT-licensed
tool, and this project does not change it. This wrapper adds the project
configuration:

- It selects the text to check: all Markdown files that Git tracks, and
  the English interface texts in web/src/locales/en.
- It applies the project glossary: "remove" and "delete" are two
  different actions, so the synonym check must not join them.

Usage:
    python3 tools/ste-lint/lint-docs.py              # check all project text
    python3 tools/ste-lint/lint-docs.py FILE [FILE]  # check some files
    python3 tools/ste-lint/lint-docs.py --json       # output for tools
    python3 tools/ste-lint/lint-docs.py --disable passive-voice

The exit code is 1 when the check finds a hard violation. Advisory
findings (passive voice, compound tenses) never make the check fail.
"""
import importlib.util
import json
import pathlib
import subprocess
import sys

sys.dont_write_bytecode = True

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent.parent
LOCALE_DIR = "web/src/locales/en/"


def load_linter():
    spec = importlib.util.spec_from_file_location("ste_lint", HERE / "ste-lint.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    groups = []
    for group in module.SYNONYM_GROUPS:
        if "delete" in group and "remove" in group:
            # Glossary: "remove" takes a card out of a deck or a location.
            # "delete" destroys data. Keep "erase" as a synonym of "delete".
            groups.append(tuple(word for word in group if word != "remove"))
        else:
            groups.append(group)
    module.SYNONYM_GROUPS = groups
    return module


def default_files():
    result = subprocess.run(
        ["git", "ls-files", "-z", "--", "*.md", LOCALE_DIR + "*.json"],
        cwd=ROOT, capture_output=True, check=True,
    )
    names = [name for name in result.stdout.decode("utf-8").split("\0") if name]
    return sorted(names)


def flatten(value, prefix=""):
    """Yield (key path, text) for each string in a translation file."""
    if isinstance(value, dict):
        for key, child in value.items():
            yield from flatten(child, f"{prefix}.{key}" if prefix else key)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from flatten(child, f"{prefix}[{index}]")
    elif isinstance(value, str):
        yield prefix, value


def lint_file(linter, path):
    candidate = pathlib.Path(path)
    full_path = candidate if candidate.exists() else ROOT / path
    text = full_path.read_text(encoding="utf-8")
    if not path.endswith(".json"):
        return linter.lint(text, filename=path)
    entries = list(flatten(json.loads(text)))
    # One interface text per line. Each text is a separate sentence group.
    joined = "\n".join(value.replace("\n", " ") for _, value in entries)
    findings, words = linter.lint(joined, filename=path)
    for finding in findings:
        key = entries[finding["line"] - 1][0] if finding["line"] <= len(entries) else "?"
        finding["file"] = f"{path} [{key}]"
    return findings, words


def main(argv):
    linter = load_linter()
    as_json = "--json" in argv
    disabled = set()
    paths = []
    index = 0
    while index < len(argv):
        arg = argv[index]
        if arg == "--disable":
            index += 1
            disabled = set(argv[index].split(","))
        elif not arg.startswith("--"):
            paths.append(arg)
        index += 1

    findings, words_total = [], 0
    for path in paths or default_files():
        file_findings, words = lint_file(linter, path)
        findings.extend(file_findings)
        words_total += words

    findings = [finding for finding in findings if finding["rule"] not in disabled]
    hard_count = sum(1 for finding in findings if finding["level"] == "advisory-free")
    linter.report(findings, words_total, as_json, hard_count, 0)
    return 1 if hard_count else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
