-- The read model of the public decks of all the shards, for the search and the exploration.
-- The domain events of the decks fill it. See docs/decisions/0028-public-deck-read-model.md.

create table users.public_decks (
    deck_id uuid primary key,
    owner_id uuid not null,
    name text not null,
    format text not null,
    description text not null default '',
    -- The color identity, with the letters W, U, B, R and G in this order. An empty text is colorless.
    color_identity text not null,
    commander_ids uuid[] not null default '{}',
    -- The Oracle IDs of the cards of the commander, companion and main zones.
    card_ids uuid[] not null default '{}',
    card_count integer not null,
    deck_version integer not null,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create index public_decks_format_idx on users.public_decks (format, updated_at desc);
create index public_decks_owner_idx on users.public_decks (owner_id, updated_at desc);
create index public_decks_commanders_idx on users.public_decks using gin (commander_ids);
create index public_decks_cards_idx on users.public_decks using gin (card_ids);

-- The copies of the public decks. The number of copies gives the popularity of a deck.
-- One row for each copy, so an event that arrives two times counts one time.
create table users.public_deck_copies (
    copy_deck_id uuid primary key,
    source_deck_id uuid not null
);

create index public_deck_copies_source_idx on users.public_deck_copies (source_deck_id);

-- The favorite decks of each user.
create table users.deck_favorites (
    account_id uuid not null,
    deck_id uuid not null,
    created_at timestamptz not null,
    primary key (account_id, deck_id)
);

create index deck_favorites_deck_idx on users.deck_favorites (deck_id);

-- The authors that each user follows.
create table users.author_follows (
    follower_id uuid not null,
    author_id uuid not null,
    created_at timestamptz not null,
    primary key (follower_id, author_id)
);

create index author_follows_author_idx on users.author_follows (author_id);
