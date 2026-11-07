# Card fixtures

These files are a small extract of the Scryfall and MTGJSON bulk files, for the tests of the card import and the card search.
The script [build_demo_data.py](../../../tools/demo-data/build_demo_data.py) creates them with the profile `fixture`.

The file `variants.json` is a small extract of the bulk file of [Commander Spellbook](https://commanderspellbook.com/), with fewer fields.
A person selected its three combos, and the script does not create it:

- `742-1295`: Thassa's Oracle and Demonic Consultation. The import keeps it.
- `1295-3093`: Thassa's Oracle and Tainted Pact. Tainted Pact is not a fixture card, so the import skips the combo.
- `513-5034--46`: Hullbreaker Horror, Sol Ring and a template. The import skips the combo, because it needs a template.

The card data comes from [Scryfall](https://scryfall.com) and [MTGJSON](https://mtgjson.com).
The combos come from [Commander Spellbook](https://commanderspellbook.com/).
Wizards of the Coast owns the content of the cards.
