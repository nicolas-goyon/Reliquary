#!/usr/bin/env python3
"""Build a small card dataset from the Scryfall and MTGJSON bulk files.

The dataset uses the file names of the local import (Cards:Import:Directory):
sets.json, cards.jsonl.gz, oracle_tags.jsonl.gz, art_tags.jsonl.gz,
rulings.jsonl.gz, card_identifiers.csv.gz and variants.json.gz.

Usage:
    python3 tools/demo-data/build_demo_data.py --source DIR --output DIR [--profile demo|fixture]

The source folder contains the downloaded bulk files:
    all_cards.jsonl.gz, oracle_tags.jsonl.gz, art_tags.jsonl.gz, rulings.jsonl.gz,
    cardIdentifiers.csv.gz and sets.json (the response of https://api.scryfall.com/sets).
    Optional: variants.json.gz, the combos of Commander Spellbook
    (https://json.commanderspellbook.com/variants.json.gz).
    The script keeps the combos whose cards are all in the dataset.
    The test fixture variants.json is not from this script: it is a small extract that a person selected.

Profiles:
    fixture  About 25 cards that cover each card layout, for the automatic tests.
    demo     The fixture cards and the most popular Commander cards, for a demo instance.
"""
import argparse
import csv
import gzip
import io
import json
import pathlib
import sys

# Cards that cover each layout, multiple names and special cases. The tests use them.
FIXTURE_CARDS = [
    "Lightning Bolt",                       # many printings and localized names
    "Sol Ring",                             # artifact, Commander staple
    "Llanowar Elves",                       # produces mana
    "Delver of Secrets // Insectile Aberration",  # transform
    "Shaile, Dean of Radiance // Embrose, Dean of Shadow",  # modal double-faced
    "Fire // Ice",                          # split
    "Ticket Booth // Tunnel of Hate",       # Room (split layout)
    "Bonecrusher Giant // Stomp",           # Adventure
    "Scathing Shadelock // Venomous Words", # prepared creature
    "Erayo, Soratami Ascendant // Erayo's Essence",  # flip
    "Bruna, the Fading Light",              # meld part
    "Gisela, the Broken Blade",             # meld part
    "Brisela, Voice of Nightmares",         # meld result
    "Atraxa, Praetors' Voice",              # commander
    "Mana Crypt",                           # banned in Commander
    "Black Lotus",                          # Reserved List
    "Aether Vial",                          # name with a ligature in old printings
    "Jötun Grunt",                          # name with an accent
    "Command Tower",                        # land
    "Counterspell",                         # many printings
    "Zilortha, Strength Incarnate",         # alternate name (Godzilla series)
    "Krenko, Mob Boss",                     # tokens
    "Cultivate",                            # ramp tag
    "Swords to Plowshares",                 # removal tag
    "Forest",                               # basic land
    "Thassa's Oracle",                      # two-card combo of Commander Spellbook
    "Demonic Consultation",                 # two-card combo of Commander Spellbook
]

FIXTURE_LANGUAGES = {"en", "fr", "ja"}
DEMO_POPULAR_COUNT = 300


def read_jsonl(path):
    with gzip.open(path, "rt", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip().rstrip(",")
            if line and line not in ("[", "]"):
                yield json.loads(line)


def oracle_id_of(card):
    return card.get("oracle_id") or (card.get("card_faces") or [{}])[0].get("oracle_id")


def select_oracle_ids(source, profile):
    names = set(FIXTURE_CARDS)
    selected = {}
    ranked = []
    for card in read_jsonl(source / "all_cards.jsonl.gz"):
        if card.get("lang") != "en":
            continue
        oracle_id = oracle_id_of(card)
        if card["name"] in names or card["name"].split(" // ")[0] in names:
            selected[oracle_id] = card["name"]
        if profile == "demo" and card.get("edhrec_rank") and card.get("layout") not in ("token", "art_series", "emblem"):
            ranked.append((card["edhrec_rank"], oracle_id, card["name"]))

    missing = names - {name for name in selected.values()} - {name.split(" // ")[0] for name in selected.values()}
    if missing:
        print(f"These fixture cards are not in the source: {sorted(missing)}", file=sys.stderr)

    if profile == "demo":
        seen = set(selected)
        for _, oracle_id, name in sorted(set(ranked)):
            if len(seen) >= len(FIXTURE_CARDS) + DEMO_POPULAR_COUNT:
                break
            if oracle_id not in seen:
                seen.add(oracle_id)
                selected[oracle_id] = name

    return selected


def related_ids(source, oracle_ids):
    """Add the tokens and the meld results of the selected cards."""
    related_printings = set()
    for card in read_jsonl(source / "all_cards.jsonl.gz"):
        if card.get("lang") == "en" and oracle_id_of(card) in oracle_ids:
            for part in card.get("all_parts", []):
                if part["component"] in ("token", "meld_result", "meld_part"):
                    related_printings.add(part["id"])
    return related_printings


def write_cards(source, output, oracle_ids, related_printings, profile):
    per_language_limit = {"en": 4, "fr": 1, "ja": 1} if profile == "fixture" else {"en": 3, "fr": 1}
    counts = {}
    printings = []
    for card in read_jsonl(source / "all_cards.jsonl.gz"):
        oracle_id = oracle_id_of(card)
        if card["id"] in related_printings:
            printings.append(card)
            oracle_ids.add(oracle_id)
            continue
        if oracle_id not in oracle_ids:
            continue
        lang = card.get("lang")
        limit = per_language_limit.get(lang, 0)
        key = (oracle_id, lang)
        if counts.get(key, 0) < limit:
            counts[key] = counts.get(key, 0) + 1
            printings.append(card)

    printings.sort(key=lambda card: (card["name"], card["lang"], card["released_at"], card["id"]))
    with gzip.open(output / "cards.jsonl.gz", "wt", encoding="utf-8") as handle:
        for card in printings:
            handle.write(json.dumps(card, ensure_ascii=False, separators=(",", ":")) + "\n")
    return printings


def write_tags(source, output, name, key, targets):
    """Write the tags with links to the targets, and all their ancestor tags.

    The search finds the cards of a tag through its descendants, so the file keeps
    the parent tags, also when they have no link to a target.
    """
    tags = {}
    kept = set()
    for tag in read_jsonl(source / f"{name}.jsonl.gz"):
        tag["taggings"] = [tagging for tagging in tag.get("taggings", []) if tagging.get(key) in targets]
        tags[tag["id"]] = tag
        if tag["taggings"]:
            kept.add(tag["id"])

    pending = list(kept)
    while pending:
        for parent_id in tags[pending.pop()].get("parent_ids") or []:
            if parent_id in tags and parent_id not in kept:
                kept.add(parent_id)
                pending.append(parent_id)

    with gzip.open(output / f"{name}.jsonl.gz", "wt", encoding="utf-8") as handle:
        for tag_id, tag in tags.items():
            if tag_id in kept:
                handle.write(json.dumps(tag, ensure_ascii=False, separators=(",", ":")) + "\n")
    return len(kept)


def write_rulings(source, output, oracle_ids):
    with gzip.open(output / "rulings.jsonl.gz", "wt", encoding="utf-8") as handle:
        for ruling in read_jsonl(source / "rulings.jsonl.gz"):
            if ruling["oracle_id"] in oracle_ids:
                handle.write(json.dumps(ruling, ensure_ascii=False, separators=(",", ":")) + "\n")


def write_identifiers(source, output, printing_ids):
    with gzip.open(source / "cardIdentifiers.csv.gz", "rt", encoding="utf-8") as reader:
        rows = csv.reader(reader)
        header = next(rows)
        column = header.index("scryfallId")
        buffer = io.StringIO()
        writer = csv.writer(buffer, lineterminator="\n")
        writer.writerow(header)
        for row in rows:
            if row[column] in printing_ids:
                writer.writerow(row)
    with gzip.open(output / "card_identifiers.csv.gz", "wt", encoding="utf-8") as handle:
        handle.write(buffer.getvalue())


def write_combos(source, output, oracle_ids):
    """Write the combos of Commander Spellbook whose cards are all in the dataset."""
    path = source / "variants.json.gz"
    if not path.exists():
        return 0
    with gzip.open(path, "rt", encoding="utf-8") as handle:
        document = json.load(handle)
    document["variants"] = [
        combo for combo in document["variants"]
        if all(use["card"].get("oracleId") in oracle_ids for use in combo["uses"])
    ]
    document["aliases"] = []
    with gzip.open(output / "variants.json.gz", "wt", encoding="utf-8") as handle:
        json.dump(document, handle, ensure_ascii=False, separators=(",", ":"))
    return len(document["variants"])


def write_sets(source, output, set_codes, profile):
    """Write the sets. The demo keeps all the sets, because the keywords b: and g: of the search use the parent sets and the blocks."""
    sets = json.loads((source / "sets.json").read_text(encoding="utf-8"))
    if profile != "demo":
        sets["data"] = [item for item in sets["data"] if item["code"] in set_codes]
    sets["has_more"] = False
    (output / "sets.json").write_text(json.dumps(sets, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    parser.add_argument("--profile", choices=("demo", "fixture"), default="demo")
    arguments = parser.parse_args()
    arguments.output.mkdir(parents=True, exist_ok=True)

    selected = select_oracle_ids(arguments.source, arguments.profile)
    oracle_ids = set(selected)
    printings = write_cards(arguments.source, arguments.output, oracle_ids, related_ids(arguments.source, oracle_ids), arguments.profile)
    illustration_ids = {card.get("illustration_id") for card in printings}
    illustration_ids |= {face.get("illustration_id") for card in printings for face in card.get("card_faces", [])}

    oracle_tags = write_tags(arguments.source, arguments.output, "oracle_tags", "oracle_id", oracle_ids)
    art_tags = write_tags(arguments.source, arguments.output, "art_tags", "illustration_id", illustration_ids)
    write_rulings(arguments.source, arguments.output, oracle_ids)
    write_identifiers(arguments.source, arguments.output, {card["id"] for card in printings})
    write_sets(arguments.source, arguments.output, {card["set"] for card in printings}, arguments.profile)
    combos = write_combos(arguments.source, arguments.output, oracle_ids) if arguments.profile == "demo" else 0

    print(f"{len(oracle_ids)} cards, {len(printings)} printings, {oracle_tags} Oracle tags, {art_tags} illustration tags, {combos} combos.")


if __name__ == "__main__":
    main()
