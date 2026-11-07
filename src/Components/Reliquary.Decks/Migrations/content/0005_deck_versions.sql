-- The deck versions, the variants and the assembled version. See docs/decisions/0033-deck-versions-and-variants.md.

-- A variant is a deck row that refers to its main deck. The deletion of the main deck deletes its variants.
alter table content.decks add column variant_of uuid references content.decks (id) on delete cascade;
alter table content.decks add column base_version_id uuid;
alter table content.decks add column head_version_id uuid;
alter table content.decks add column assembled_version_id uuid;

create index decks_variant_of_idx on content.decks (variant_of) where variant_of is not null;

create table content.deck_versions (
    id uuid primary key,
    deck_id uuid not null references content.decks (id) on delete cascade,
    owner_id uuid not null,
    -- The number of the version in its deck: 1, 2, 3 and more.
    number integer not null,
    kind text not null check (kind in ('commit', 'merge', 'revert', 'restore')),
    message text not null,
    -- The parent versions: none for the first version, one for a commit, two for a merge.
    parent_ids uuid[] not null default '{}',
    created_at timestamptz not null,
    unique (deck_id, number)
);

-- The frozen lines of each version. The tags are names, because a user can rename or delete a tag later.
create table content.deck_version_lines (
    version_id uuid not null references content.deck_versions (id) on delete cascade,
    owner_id uuid not null,
    oracle_id uuid not null,
    printing_id uuid,
    finish text,
    quantity integer not null,
    zone text not null,
    note text not null default '',
    categories text[] not null default '{}',
    tags text[] not null default '{}',
    global_tags text[] not null default '{}'
);

create index deck_version_lines_version_idx on content.deck_version_lines (version_id);

-- A deck version never changes. Only the deletion of its deck deletes it: the cascade deletes the deck row first.
create function content.refuse_version_change() returns trigger language plpgsql as $$
begin
    if tg_op = 'UPDATE' or exists (select 1 from content.decks where id = old.deck_id) then
        raise exception 'A deck version never changes, and only the deletion of its deck deletes it.' using errcode = 'restrict_violation';
    end if;
    return old;
end;
$$;

create function content.refuse_version_line_change() returns trigger language plpgsql as $$
begin
    if tg_op = 'UPDATE' or exists (select 1 from content.deck_versions where id = old.version_id) then
        raise exception 'A deck version line never changes, and only the deletion of its version deletes it.' using errcode = 'restrict_violation';
    end if;
    return old;
end;
$$;

create trigger deck_versions_frozen before update or delete on content.deck_versions
    for each row execute function content.refuse_version_change();
create trigger deck_version_lines_frozen before update or delete on content.deck_version_lines
    for each row execute function content.refuse_version_line_change();
