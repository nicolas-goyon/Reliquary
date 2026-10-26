-- The card reference data of the Cards component. See docs/architecture/data-model.md.
-- The daily import from Scryfall and MTGJSON fills these tables. All users share them in read-only mode.

create extension if not exists pg_trgm;

-- Sets of cards. Source: the Scryfall sets API.
create table reference.sets (
    code text primary key,
    scryfall_id uuid not null unique,
    name text not null,
    set_type text not null,
    released_at date,
    block_code text,
    block text,
    parent_set_code text,
    card_count integer not null default 0,
    digital boolean not null default false,
    icon_svg_uri text,
    updated_at timestamptz not null
);

-- One row for each logical card (Oracle identity).
-- For a card with more than one face, the text columns contain all the faces, separated by " // ".
create table reference.oracle_cards (
    id uuid primary key,
    name text not null,
    name_normalized text not null,
    layout text not null,
    mana_cost text,
    mana_value numeric not null default 0,
    type_line text,
    oracle_text text,
    colors text[] not null default '{}',
    colors_mask smallint not null default 0,
    color_identity text[] not null default '{}',
    color_identity_mask smallint not null default 0,
    color_indicator text[],
    produced_mana text[] not null default '{}',
    produced_mana_mask smallint not null default 0,
    keywords text[] not null default '{}',
    power text,
    toughness text,
    loyalty text,
    defense text,
    hand_modifier text,
    life_modifier text,
    legalities jsonb not null default '{}',
    reserved boolean not null default false,
    game_changer boolean not null default false,
    edhrec_rank integer,
    penny_rank integer,
    is_extra boolean not null default false,
    first_released_at date,
    default_printing_id uuid,
    content_hash bytea not null,
    updated_at timestamptz not null
);

create index oracle_cards_name_idx on reference.oracle_cards (name_normalized);
create index oracle_cards_mana_value_idx on reference.oracle_cards (mana_value);
create index oracle_cards_colors_idx on reference.oracle_cards (colors_mask);
create index oracle_cards_identity_idx on reference.oracle_cards (color_identity_mask);
create index oracle_cards_legalities_idx on reference.oracle_cards using gin (legalities jsonb_path_ops);
create index oracle_cards_keywords_idx on reference.oracle_cards using gin (keywords);
create index oracle_cards_type_trgm_idx on reference.oracle_cards using gin (lower(type_line) gin_trgm_ops);
create index oracle_cards_text_trgm_idx on reference.oracle_cards using gin (lower(oracle_text) gin_trgm_ops);

-- Each face or half of a card: double-faced, flip, split, Room, Adventure, prepared and meld cards.
-- A card with one face has one row with the index 0.
create table reference.card_faces (
    oracle_id uuid not null references reference.oracle_cards (id) on delete cascade,
    face_index smallint not null,
    name text not null,
    mana_cost text,
    mana_value numeric not null default 0,
    type_line text,
    oracle_text text,
    colors text[] not null default '{}',
    colors_mask smallint not null default 0,
    color_indicator text[],
    power text,
    toughness text,
    loyalty text,
    defense text,
    primary key (oracle_id, face_index)
);

-- All the names of a card: the Oracle name, the face names, the localized names and the alternate names.
-- The import and the search use the same normalization: lowercase, no accents, simple punctuation.
create table reference.card_names (
    oracle_id uuid not null references reference.oracle_cards (id) on delete cascade,
    name text not null,
    name_normalized text not null,
    lang text not null,
    kind text not null check (kind in ('oracle', 'face', 'printed', 'flavor')),
    primary key (oracle_id, name_normalized, lang, kind)
);

create index card_names_normalized_idx on reference.card_names (name_normalized);
create index card_names_trgm_idx on reference.card_names using gin (name_normalized gin_trgm_ops);

-- Each printing of a card: one set, one collector number and one language.
create table reference.printings (
    id uuid primary key,
    oracle_id uuid not null references reference.oracle_cards (id),
    set_code text not null,
    set_name text not null,
    set_type text not null,
    collector_number text not null,
    collector_number_value integer,
    lang text not null,
    released_at date not null,
    rarity text not null,
    rarity_rank smallint not null,
    finishes text[] not null default '{}',
    games text[] not null default '{}',
    digital boolean not null default false,
    promo boolean not null default false,
    promo_types text[] not null default '{}',
    reprint boolean not null default false,
    variation boolean not null default false,
    oversized boolean not null default false,
    full_art boolean not null default false,
    textless boolean not null default false,
    booster boolean not null default false,
    frame text,
    frame_effects text[] not null default '{}',
    border_color text,
    security_stamp text,
    watermark text,
    artist text,
    illustration_id uuid,
    flavor_text text,
    flavor_name text,
    printed_name text,
    printed_type_line text,
    printed_text text,
    image_status text,
    image_uris jsonb,
    prices jsonb not null default '{}',
    multiverse_ids integer[] not null default '{}',
    mtgo_id integer,
    arena_id integer,
    tcgplayer_id integer,
    cardmarket_id integer,
    external_ids jsonb not null default '{}',
    content_hash bytea not null,
    updated_at timestamptz not null
);

create index printings_oracle_idx on reference.printings (oracle_id);
create index printings_set_idx on reference.printings (set_code, collector_number);
create index printings_lang_idx on reference.printings (lang);
create index printings_released_idx on reference.printings (released_at);
create index printings_illustration_idx on reference.printings (illustration_id);
create index printings_artist_trgm_idx on reference.printings using gin (lower(artist) gin_trgm_ops);

-- The faces of a printing: localized text, art and images of each face.
create table reference.printing_faces (
    printing_id uuid not null references reference.printings (id) on delete cascade,
    face_index smallint not null,
    name text not null,
    printed_name text,
    printed_type_line text,
    printed_text text,
    flavor_text text,
    artist text,
    illustration_id uuid,
    watermark text,
    image_uris jsonb,
    primary key (printing_id, face_index)
);

-- Links between cards: tokens, meld parts and meld results, and combo pieces.
create table reference.card_relations (
    oracle_id uuid not null references reference.oracle_cards (id) on delete cascade,
    related_printing_id uuid not null,
    related_name text not null,
    component text not null,
    primary key (oracle_id, related_printing_id, component)
);

-- The tags of Scryfall Tagger: Oracle tags for the function of a card, illustration tags for its art.
create table reference.tags (
    id uuid primary key,
    kind text not null check (kind in ('oracle', 'illustration')),
    slug text not null,
    label text not null,
    description text,
    parent_ids uuid[] not null default '{}',
    aliases text[] not null default '{}'
);

create unique index tags_kind_slug_idx on reference.tags (kind, slug);

-- Each tag and all its descendants, so a search with a tag also finds the cards of its child tags.
create table reference.tag_closure (
    ancestor_id uuid not null,
    descendant_id uuid not null,
    primary key (ancestor_id, descendant_id)
);

create table reference.oracle_taggings (
    tag_id uuid not null,
    oracle_id uuid not null,
    weight text not null,
    primary key (tag_id, oracle_id)
);

create index oracle_taggings_oracle_idx on reference.oracle_taggings (oracle_id);

create table reference.illustration_taggings (
    tag_id uuid not null,
    illustration_id uuid not null,
    weight text not null,
    primary key (tag_id, illustration_id)
);

create index illustration_taggings_illustration_idx on reference.illustration_taggings (illustration_id);

create table reference.rulings (
    oracle_id uuid not null,
    source text not null,
    published_at date not null,
    comment text not null
);

create index rulings_oracle_idx on reference.rulings (oracle_id);

-- One row for each run of the import of one source file.
create table reference.import_runs (
    id uuid primary key,
    source text not null,
    source_updated_at timestamptz,
    started_at timestamptz not null,
    finished_at timestamptz,
    status text not null,
    item_count integer,
    error text
);

create index import_runs_source_idx on reference.import_runs (source, started_at desc);

-- Staging tables of the import. The import fills them with COPY, then changes the real tables in one transaction.
-- Only one import runs at a time, because the scheduled job has a lock.
create unlogged table reference.import_oracle_cards (like reference.oracle_cards including defaults);
create unlogged table reference.import_card_faces (like reference.card_faces including defaults);
create unlogged table reference.import_card_names (like reference.card_names including defaults);
create unlogged table reference.import_card_relations (like reference.card_relations including defaults);
create unlogged table reference.import_printings (like reference.printings including defaults);
create unlogged table reference.import_printing_faces (like reference.printing_faces including defaults);

-- The import sets the time of the change when it copies the rows into the real tables.
alter table reference.import_oracle_cards alter column updated_at drop not null;
alter table reference.import_printings alter column updated_at drop not null;
