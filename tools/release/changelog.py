"""Moves the section "Unreleased" of CHANGELOG.md to a release section.

Usage: python3 tools/release/changelog.py VERSION [DATE]

The release workflow runs this script on the branch of the release pull request.
The script adds the heading "## [VERSION] - DATE" below the heading "## Unreleased".
Thus the notes of the section "Unreleased" become the notes of the release, and the section "Unreleased" becomes empty.
When the file already has a section for VERSION, the script changes nothing.
"""

import datetime
import pathlib
import re
import sys

CHANGELOG = pathlib.Path(__file__).resolve().parents[2] / "CHANGELOG.md"
UNRELEASED = "## Unreleased"


def release(text: str, version: str, date: str) -> str:
    if re.search(rf"^## \[{re.escape(version)}\]", text, flags=re.MULTILINE):
        return text
    if f"\n{UNRELEASED}\n" not in text:
        raise SystemExit(f'CHANGELOG.md has no heading "{UNRELEASED}".')
    return text.replace(f"\n{UNRELEASED}\n", f"\n{UNRELEASED}\n\n## [{version}] - {date}\n", 1)


def main() -> None:
    if len(sys.argv) not in (2, 3):
        raise SystemExit(__doc__)
    version = sys.argv[1].removeprefix("v")
    date = sys.argv[2] if len(sys.argv) == 3 else datetime.date.today().isoformat()
    text = CHANGELOG.read_text(encoding="utf-8")
    CHANGELOG.write_text(release(text, version, date), encoding="utf-8")


if __name__ == "__main__":
    main()
