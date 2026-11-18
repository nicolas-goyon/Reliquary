-- The last catalog index that the instance accepted. See docs/architecture/extensions.md.
-- The table has one row at most. The catalog sync refuses an index with a smaller serial than this row.
create table users.plugin_catalog (
    singleton boolean primary key default true check (singleton),
    serial bigint not null check (serial >= 1),
    expires_at timestamptz not null,
    key_id text not null,
    -- The SHA-256 hash of the bytes of the index. An index with the same serial must have the same hash.
    index_sha256 bytea not null,
    -- The exact bytes of index.json and index.json.sig, so the administrator can check them again.
    index_content bytea not null,
    signature bytea not null,
    -- The time of the first acceptance of this serial, and the time of the last sync that got it.
    accepted_at timestamptz not null,
    checked_at timestamptz not null
);
