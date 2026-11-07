-- The decks of each owner, in the shard of the owner. See docs/decisions/0026-deck-model.md.

-- The folders of the decks. A folder can contain other folders.
create table content.deck_folders (
    id uuid primary key,
    owner_id uuid not null,
    parent_id uuid references content.deck_folders (id) on delete cascade,
    name text not null,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create index deck_folders_owner_idx on content.deck_folders (owner_id);

create table content.decks (
    id uuid primary key,
    owner_id uuid not null,
    name text not null,
    format text not null,
    visibility text not null check (visibility in ('private', 'unlisted', 'public')),
    folder_id uuid references content.deck_folders (id) on delete set null,
    description text not null default '',
    -- The deck that the user copied, when the deck is a copy.
    source_deck_id uuid,
    pinned boolean not null default false,
    archived_at timestamptz,
    -- Each change of the deck or of its lines increases the version.
    version integer not null default 1,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create index decks_owner_idx on content.decks (owner_id, updated_at desc, id desc);

-- The working list of a deck. Milestone M2 adds the frozen deck versions.
create table content.deck_lines (
    id uuid primary key,
    deck_id uuid not null references content.decks (id) on delete cascade,
    owner_id uuid not null,
    oracle_id uuid not null,
    printing_id uuid,
    finish text check (finish in ('nonfoil', 'foil', 'etched')),
    quantity integer not null check (quantity between 1 and 999),
    zone text not null check (zone in ('commander', 'companion', 'main', 'sideboard', 'considering', 'tokens')),
    note text not null default '',
    created_at timestamptz not null,
    updated_at timestamptz not null
);

-- One line for each card, printing and finish in a zone.
create unique index deck_lines_key_idx on content.deck_lines (
    deck_id, zone, oracle_id, coalesce(printing_id, '00000000-0000-0000-0000-000000000000'::uuid), coalesce(finish, '')
);
