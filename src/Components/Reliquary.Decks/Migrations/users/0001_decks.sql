-- The owner of each deck. A link to an unlisted or public deck contains only the deck ID,
-- so the API needs the owner to find the shard of the deck. See docs/decisions/0026-deck-model.md.
create table users.deck_directory (
    deck_id uuid primary key,
    owner_id uuid not null
);

create index deck_directory_owner_idx on users.deck_directory (owner_id);
