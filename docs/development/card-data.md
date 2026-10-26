# Card data

This guide tells how to import the card data into a local instance.
Terms follow the [glossary](../glossary.md).
[ADR 0020](../decisions/0020-card-data-import.md) gives the reasons for the import design.

## Sources

The scheduled job `cards.import` imports these files:

| File | Source | Content |
| --- | --- | --- |
| `sets.json` | Scryfall sets API | The sets |
| `cards.jsonl` | Scryfall bulk file `all_cards` or `default_cards` | The cards, their faces, their names and their printings |
| `oracle_tags.jsonl` | Scryfall bulk file `oracle_tags` | The Oracle tags and their links to the cards |
| `art_tags.jsonl` | Scryfall bulk file `art_tags` | The illustration tags and their links to the illustrations |
| `rulings.jsonl` | Scryfall bulk file `rulings` | The rulings |
| `card_identifiers.csv` | MTGJSON `csv/cardIdentifiers.csv.gz` | The IDs of each printing on other sites |

Each file can have the extension `.gz`.

## Import the full data

The worker runs the job every 12 hours.
To run it now, use the CLI:

```shell
just cli jobs run cards.import --force
```

The job downloads about 450 MB and needs about 3 GB in the database.

To download less, use the smaller card file:

```shell
Cards__Import__BulkType=default_cards just cli jobs run cards.import --force
```

## Import local files

To work without the network, put the files in a folder and set `Cards:Import:Directory`:

```shell
Cards__Import__Directory=/path/to/folder just cli jobs run cards.import --force
```

A missing file skips its step.

## Create a small dataset

The script `tools/demo-data/build_demo_data.py` creates a small dataset from the downloaded files.

1. Download the bulk files of Scryfall and the identifiers of MTGJSON into a folder.
   The [Scryfall bulk data API](https://scryfall.com/docs/api/bulk-data) gives the addresses.
2. Save the response of `https://api.scryfall.com/sets` as `sets.json` in the same folder.
3. Run the script:

   ```shell
   python3 tools/demo-data/build_demo_data.py --source /path/to/downloads --output /path/to/dataset --profile demo
   ```

The profile `fixture` creates the test data of `tests/Reliquary.Cards.Tests/Fixtures`.
The profile `demo` adds the 300 most popular Commander cards, and keeps all the sets.
The folder [`data/demo`](../../data/demo/README.md) contains the result of the profile `demo`.

The tag files keep the parent tags of each tag, also when a parent tag has no card of the dataset.
The search uses the parent tags: `otag:removal` finds the cards of all the child tags of `removal`.

## After the migration of the card search

The migration `0002_card_search.sql` adds columns that the import fills.
After this migration, run the import again with `--force`:

```shell
just cli jobs run cards.import --force
```

Until this import, the search uses the printings in all the languages, and `is:spotlight` finds no card.

## Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| `Cards:Import:Enabled` | `true` | The worker runs the job. |
| `Cards:Import:Interval` | `12:00:00` | The minimum time between two imports. |
| `Cards:Import:BulkType` | `all_cards` | The card file of Scryfall: `all_cards` or `default_cards`. |
| `Cards:Import:Directory` | — | A folder with local files. The job then downloads nothing. |
| `Cards:Import:IncludeIllustrationTags` | `true` | The job imports the illustration tags. |
| `Cards:Import:IncludeMtgjsonIdentifiers` | `true` | The job imports the identifiers of MTGJSON. |
| `Cards:Import:DownloadDirectory` | The temporary folder | The folder for the downloaded files. |

## Rules of Scryfall

- The import sends a `User-Agent` header and an `Accept` header, as Scryfall asks.
- Reliquary shows the images from the Scryfall servers. It does not store them.
- The prices of Scryfall are estimates. Do not use them for a sale.
