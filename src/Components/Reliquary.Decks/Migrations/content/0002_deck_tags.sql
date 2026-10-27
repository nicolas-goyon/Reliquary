-- The tags of the deck lines and the categories of the analysis. See docs/decisions/0026-deck-model.md.

-- A global tag has no deck: the user uses it in all their decks. A local tag belongs to one deck.
create table content.tags (
    id uuid primary key,
    owner_id uuid not null,
    deck_id uuid references content.decks (id) on delete cascade,
    name text not null,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create unique index tags_name_idx on content.tags (owner_id, coalesce(deck_id, '00000000-0000-0000-0000-000000000000'::uuid), lower(name));

create table content.deck_line_tags (
    deck_line_id uuid not null references content.deck_lines (id) on delete cascade,
    tag_id uuid not null references content.tags (id) on delete cascade,
    primary key (deck_line_id, tag_id)
);

create index deck_line_tags_tag_idx on content.deck_line_tags (tag_id);

-- The predefined categories of the analysis.
alter table content.deck_lines add column categories text[] not null default '{}'
    check (categories <@ array['ramp', 'card_draw', 'targeted_interaction', 'board_wipe', 'synergy', 'land', 'win_condition', 'protection']);
