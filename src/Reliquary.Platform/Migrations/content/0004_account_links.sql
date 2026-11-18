-- The linked accounts of each user: one row for each plugin and each external site. See docs/decisions/0051-linked-accounts.md.
-- The vault encrypts the secret with the key of key_id. A link with a public user name has no secret.
create table content.account_links (
    owner_id uuid not null,
    extension_id text not null,
    site text not null,
    auth_method text not null,
    -- The name of the account on the external site: the public user name, or an optional name for an API key.
    display_name text,
    secret_ciphertext bytea,
    key_id text,
    -- linked: no sync yet. synced: the last sync completed. failed: the last sync failed.
    state text not null,
    last_sync_at timestamptz,
    last_sync_error text,
    linked_at timestamptz not null,
    updated_at timestamptz not null,
    primary key (owner_id, extension_id, site),
    constraint account_links_secret_check check ((secret_ciphertext is null) = (key_id is null))
);
