#!/usr/bin/env python3
"""Compare the card search of a running Reliquary API with recorded Scryfall results.

The script runs each query of an expectations file on the endpoint /api/v1/cards/search,
reads all the pages, and compares the Oracle IDs with the recorded Oracle IDs.
Use it with a database of the full card data and a file that record_expectations.py
wrote with --full.

Usage:
    python3 tools/search-parity/compare_api.py --expectations FILE [--api http://localhost:5080] [--query "q"]

The script uses the Python standard library only.
"""
import argparse
import json
import pathlib
import sys
import urllib.error
import urllib.parse
import urllib.request


def search(api, query):
    """Return (Oracle IDs and names, warnings), or (None, problem) when the API rejects the query."""
    found = {}
    cursor = None
    while True:
        parameters = {"q": query, "limit": "100", "unique": "cards"}
        if cursor:
            parameters["cursor"] = cursor
        try:
            with urllib.request.urlopen(f"{api}/api/v1/cards/search?{urllib.parse.urlencode(parameters)}", timeout=120) as response:
                page = json.load(response)
        except urllib.error.HTTPError as error:
            problem = json.load(error)
            if problem.get("code") == "search_query_empty":
                return {}, problem.get("warnings")
            return None, problem
        for item in page["items"]:
            found[item["card"]["id"]] = item["card"]["name"]
        cursor = page.get("nextCursor")
        if not cursor:
            return found, page.get("warnings")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--expectations", type=pathlib.Path, required=True)
    parser.add_argument("--api", default="http://localhost:5080")
    parser.add_argument("--query", action="append", help="Compare only this query.")
    arguments = parser.parse_args()

    entries = json.loads(arguments.expectations.read_text(encoding="utf-8"))["queries"]
    exact = 0
    compared = 0
    for entry in entries:
        query = entry["query"]
        if arguments.query and query not in arguments.query:
            continue
        compared += 1
        found, details = search(arguments.api, query)
        if found is None:
            print(f"ERROR  {query}: {details.get('code')} {details.get('detail')}")
            continue
        expected = set(entry["expected"])
        missing = expected - found.keys()
        extra = found.keys() - expected
        if not missing and not extra:
            exact += 1
            print(f"EXACT  {query} ({len(expected)} cards)")
            continue
        print(f"DIFF   {query}: expected {len(expected)}, found {len(found)}, missing {len(missing)}, not expected {len(extra)}")
        if missing:
            print(f"       missing: {sorted(missing)[:5]}")
        if extra:
            print(f"       not expected: {sorted(found[oracle_id] for oracle_id in extra)[:5]}")
    print(f"{exact} of {compared} queries find the same cards.")
    return 0 if exact == compared else 1


if __name__ == "__main__":
    sys.exit(main())
