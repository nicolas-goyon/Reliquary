-- The combos of Commander Spellbook. See docs/decisions/0030-combos-from-commander-spellbook.md.
-- The import of the card data fills this table from the bulk file of Commander Spellbook.
-- The table contains only the combos whose cards are all known Oracle cards and that need no template card.
create table reference.combos (
    -- The ID of the combo on Commander Spellbook, for example "742-1295".
    id text primary key,
    -- The Oracle cards of the combo, in the order of Commander Spellbook, with no repetition.
    oracle_ids uuid[] not null,
    -- The number of copies of each card, in the order of oracle_ids.
    quantities integer[] not null,
    -- The names of the results, for example "Win the game".
    results text[] not null,
    -- True when a result is infinite or near-infinite, or when Commander Spellbook marks it as a standalone result.
    game_ending boolean not null,
    mana_needed text,
    mana_value_needed integer not null default 0,
    popularity integer,
    updated_at timestamptz not null
);

-- The combo search finds the combos that share a card with a deck.
create index combos_oracle_ids_idx on reference.combos using gin (oracle_ids);

-- The staging table of the import.
create unlogged table reference.import_combos (like reference.combos including defaults);
alter table reference.import_combos alter column updated_at drop not null;
