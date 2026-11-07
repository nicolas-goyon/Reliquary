#!/usr/bin/env python3
"""Record the results of the Scryfall search for the parity tests of the card search.

For each query of queries.json, the script asks the Scryfall search API for all the
printings that match. Then it keeps the Oracle cards that have a matching printing
in the demo dataset, and writes the expected Oracle IDs to expectations.json.

The script uses the printings, not only the Oracle cards, because the demo dataset
contains only some printings of each card. A card matches in Reliquary only when one
of its printings in the dataset matches.

With --full, the script records all the Oracle cards that match, for a comparison
with a database of the full card data (see tools/search-parity/compare_api.py).

Usage:
    python3 tools/search-parity/record_expectations.py
        [--queries tools/search-parity/queries.json]
        [--demo data/demo]
        [--output tests/Reliquary.Cards.Tests/SearchParity/expectations.json]
        [--only "query"] [--full]

The script uses the Python standard library only. It sends at most 10 requests each second,
as the Scryfall API asks.
"""
import argparse
import concurrent.futures
import datetime
import gzip
import http.client
import json
import pathlib
import re
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

API = "https://api.scryfall.com/cards/search"
HEADERS = {"User-Agent": "Reliquary/0.1 (+https://github.com/nicolas-goyon/reliquary)", "Accept": "application/json"}
DELAY_SECONDS = 0.11
MAX_PAGES = 12
BATCH_SIZE = 20
WORKERS = 8

# The display keywords do not change which cards match. The script removes them,
# so that the API returns all the matching printings.
DISPLAY_KEYWORDS = re.compile(r"(?:(?<=\s)|^)(?:unique|order|sort|dir|direction|prefer|display):\S+|(?:(?<=\s)|^)(?:\+\+|@@)(?=\s|$)", re.IGNORECASE)

ROOT = pathlib.Path(__file__).resolve().parents[2]


class Scryfall:
    """A client of the Scryfall API. Several threads can use it. It starts at most one request in each interval of DELAY_SECONDS."""

    def __init__(self):
        self.lock = threading.Lock()
        self.next_start = 0.0
        self.requests = 0

    def wait_for_turn(self):
        with self.lock:
            now = time.monotonic()
            start = max(now, self.next_start)
            self.next_start = start + DELAY_SECONDS
            self.requests += 1
        if start > now:
            time.sleep(start - now)

    def get(self, url):
        request = urllib.request.Request(url, headers=HEADERS)
        for attempt in range(5):
            self.wait_for_turn()
            try:
                with urllib.request.urlopen(request, timeout=60) as response:
                    return json.load(response)
            except urllib.error.HTTPError as error:
                if error.code == 429 or error.code >= 500:
                    time.sleep(2 ** attempt)
                    continue
                return json.load(error)
            except (urllib.error.URLError, TimeoutError, ConnectionError, http.client.IncompleteRead, json.JSONDecodeError):
                time.sleep(2 ** attempt)
        raise RuntimeError(f"Scryfall did not answer: {url}")

    def search(self, query, max_pages=None, unique="prints"):
        """Return (cards, total, warnings, complete). complete is False when the result has more pages than max_pages."""
        parameters = {"q": query, "unique": unique, "include_extras": "true" if "include:extras" in query.lower() else "false"}
        url = API + "?" + urllib.parse.urlencode(parameters)
        printings, warnings, total, pages = [], [], 0, 0
        while url:
            page = self.get(url)
            if page.get("object") == "error":
                if page.get("status") == 404:
                    return [], 0, page.get("warnings") or [], True
                raise RuntimeError(f"Scryfall rejected the query {query!r}: {page.get('details')}")
            total = page.get("total_cards", 0)
            warnings = page.get("warnings") or warnings
            printings.extend(page["data"])
            pages += 1
            url = page.get("next_page") if page.get("has_more") else None
            if url and max_pages is not None and total > max_pages * len(page["data"]):
                return printings, total, warnings, False
        return printings, total, warnings, True


def read_jsonl(path):
    with gzip.open(path, "rt", encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                yield json.loads(line)


def oracle_id_of(card):
    return card.get("oracle_id") or (card.get("card_faces") or [{}])[0].get("oracle_id")


class Demo:
    """The printings of the demo dataset, and the rule of the default language of Reliquary."""

    def __init__(self, folder):
        self.by_key = {}
        self.oracle_ids = set()
        english = set()
        extra_layouts = {"token", "double_faced_token", "emblem", "art_series", "planar", "scheme", "vanguard"}
        game_cards = set()
        self.extra_ids = set()
        for card in read_jsonl(folder / "cards.jsonl.gz"):
            oracle_id = oracle_id_of(card)
            self.by_key[(card["set"], card["collector_number"], card["lang"])] = oracle_id
            self.oracle_ids.add(oracle_id)
            if card["lang"] == "en":
                english.add((card["set"], card["collector_number"]))
            # The same rule as the import: an extra card has an extra layout, or only printings in token or memorabilia sets.
            if card["layout"] in extra_layouts:
                self.extra_ids.add(oracle_id)
            elif card["set_type"] not in ("token", "memorabilia"):
                game_cards.add(oracle_id)
        self.extra_ids |= self.oracle_ids - game_cards
        # In Reliquary, a printing is in the default language when it is in English,
        # or when the dataset has no English printing with the same set and collector number.
        self.other_default_languages = {lang for (set_code, number, lang) in self.by_key if lang != "en" and (set_code, number) not in english}
        self.default_keys = {key for key in self.by_key if key[2] == "en" or (key[0], key[1]) not in english}

    def match(self, printing):
        """Return the Oracle ID of the printing when the demo dataset contains it in the default language of Reliquary."""
        key = (printing["set"], printing["collector_number"], printing["lang"])
        return self.by_key[key] if key in self.default_keys else None


def strip_display(query):
    return " ".join(DISPLAY_KEYWORDS.sub(" ", query).split())


INCLUDE_KEYWORD = re.compile(r"(?:(?<=\s)|^)include:\S+", re.IGNORECASE)


def combine(query, condition):
    """Add a condition to a query. Scryfall refuses include: inside parentheses, so it stays outside."""
    includes = INCLUDE_KEYWORD.findall(query)
    rest = " ".join(INCLUDE_KEYWORD.sub(" ", query).split())
    return " ".join([f"({rest})", condition, *includes])


# These keywords make Scryfall show the extra cards and the printings of the memorabilia sets, as include:extras does.
EXTRAS_KEYWORDS = re.compile(r"(?:^|[\s(])(?:include:extras|a:|artist:|banned:|restricted:|name:|is:reserved|is:funny|is:dfc)", re.IGNORECASE)
LANGUAGE_KEYWORDS = re.compile(r"(?:^|[\s(-])(?:lang|language):", re.IGNORECASE)


def matching_printings(scryfall, demo, query):
    printings, total, warnings, complete = scryfall.search(query, max_pages=MAX_PAGES)
    if complete:
        return printings, total, warnings
    # The result is large. Ask again for the demo cards only, in batches of Oracle IDs.
    # The keyword oracleid: makes Scryfall show the extra cards and the memorabilia printings.
    # The script removes them again, because the query without oracleid: does not show them.
    extras = bool(EXTRAS_KEYWORDS.search(query))
    printings = []
    ids = sorted(demo.oracle_ids if extras else demo.oracle_ids - demo.extra_ids)
    for start in range(0, len(ids), BATCH_SIZE):
        batch = " or ".join(f"oracleid:{oracle_id}" for oracle_id in ids[start:start + BATCH_SIZE])
        found, _, _, _ = scryfall.search(combine(query, f"({batch})"))
        printings.extend(found if extras else [printing for printing in found if printing.get("set_type") != "memorabilia"])
    return printings, total, warnings


def expected_ids(scryfall, demo, query):
    printings, total, warnings = matching_printings(scryfall, demo, query)
    # Reliquary also searches the printings in another language when the dataset has no English printing
    # with the same set and collector number. Scryfall shows them only with lang:, so the script asks for them.
    if not LANGUAGE_KEYWORDS.search(query):
        for lang in sorted(demo.other_default_languages):
            found, _, _ = matching_printings(scryfall, demo, combine(query, f"lang:{lang}"))
            printings.extend(found)
    matched = {demo.match(printing) for printing in printings}
    matched.discard(None)
    return sorted(matched), total, warnings


def full_ids(scryfall, query):
    """Return the Oracle IDs of all the cards that match, for a comparison with a database of the full card data."""
    cards, total, warnings, _ = scryfall.search(query, unique="cards")
    return sorted({oracle_id_of(card) for card in cards}), total, warnings


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--queries", type=pathlib.Path, default=ROOT / "tools/search-parity/queries.json")
    parser.add_argument("--demo", type=pathlib.Path, default=ROOT / "data/demo")
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "tests/Reliquary.Cards.Tests/SearchParity/expectations.json")
    parser.add_argument("--only", action="append", help="Record only this query. Keep the other entries of the output file.")
    parser.add_argument("--full", action="store_true", help="Record all the matching Oracle cards, not only the cards of the demo dataset. Use it with --output.")
    arguments = parser.parse_args()

    queries = json.loads(arguments.queries.read_text(encoding="utf-8"))["queries"]
    previous = {}
    if arguments.only and arguments.output.exists():
        previous = {entry["query"]: entry for entry in json.loads(arguments.output.read_text(encoding="utf-8"))["queries"]}

    demo = Demo(arguments.demo)
    scryfall = Scryfall()

    def record(item):
        query = item["query"]
        if arguments.only and query not in arguments.only and query in previous:
            entry = previous[query]
            entry.update({key: item.get(key) for key in ("section", "origin", "knownDifference")})
            return entry
        if arguments.full:
            ids, total, warnings = full_ids(scryfall, strip_display(query))
        else:
            ids, total, warnings = expected_ids(scryfall, demo, strip_display(query))
        print(f"{len(ids):5d} of {total:6d}  {query}", file=sys.stderr)
        return {
            "query": query,
            "section": item["section"],
            "origin": item["origin"],
            "scryfallPrintings": total,
            "scryfallWarnings": warnings,
            "expected": ids,
            "knownDifference": item.get("knownDifference"),
        }

    # A search request takes about 2 seconds. Parallel queries keep the rate near the limit of 10 requests each second.
    with concurrent.futures.ThreadPoolExecutor(max_workers=WORKERS) as executor:
        entries = list(executor.map(record, queries))

    document = {
        "source": "Scryfall search API (https://api.scryfall.com/cards/search)" + ("" if arguments.full else ", intersected with the printings of data/demo"),
        "recordedAt": datetime.date.today().isoformat(),
        "queries": entries,
    }
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(document, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{len(entries)} queries, {scryfall.requests} requests.", file=sys.stderr)


if __name__ == "__main__":
    main()
