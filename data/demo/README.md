# Demo card data

This folder contains a small extract of the Scryfall and MTGJSON bulk files.
The search parity tests use it, and a demo instance can use it later.
The script [build_demo_data.py](../../tools/demo-data/build_demo_data.py) creates it with the profile `demo`.

The dataset contains 342 Oracle cards and 1,476 printings:

- The cards of the test fixtures and the 300 most popular Commander cards on EDHREC.
- At most 3 English printings and 1 French printing of each card.
- The tokens and the meld results of these cards.
- The Oracle tags and the illustration tags of these cards, with all their parent tags.
- All the sets of Scryfall, so that the keywords `b:` and `g:` of the search find the blocks and the parent sets.

The files use the names of the local import.
To import them, set `Cards:Import:Directory` to this folder:

```shell
Cards__Import__Directory=data/demo just cli jobs run cards.import --force
```

The card data comes from [Scryfall](https://scryfall.com) and [MTGJSON](https://mtgjson.com).
Wizards of the Coast owns the content of the cards.
