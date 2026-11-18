-- The storage of each plugin for each user: small values by key (issue #279).
-- The plugin frame has no storage of its own, so the core keeps these values in the database of the user.
-- The API limits the keys and the values of one plugin and one user to 64 KB.
create table content.plugin_storage (
    owner_id uuid not null,
    extension_id text not null,
    key text not null,
    value jsonb not null,
    updated_at timestamptz not null,
    primary key (owner_id, extension_id, key)
);
