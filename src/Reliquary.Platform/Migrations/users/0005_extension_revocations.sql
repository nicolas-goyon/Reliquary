-- The plugin versions that the plugin catalog revoked. See docs/architecture/extensions.md.
-- A revocation is permanent: the catalog sync adds rows and never deletes them. A fixed plugin gets a new version.
create table users.extension_revocations (
    extension_id text not null,
    version text not null,
    reason text not null,
    -- The time when the catalog sync got the revocation.
    revoked_at timestamptz not null,
    primary key (extension_id, version)
);
