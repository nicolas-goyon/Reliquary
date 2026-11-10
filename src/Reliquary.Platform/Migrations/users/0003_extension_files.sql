-- The files of each plugin version. The API sends them to the plugin frames. See docs/architecture/extensions.md.
-- The rows of a version do not change: a new version of a plugin has new rows.
create table users.extension_files (
    extension_id text not null,
    version text not null,
    -- The path relative to the root of the plugin files, for example dist/index.js.
    path text not null,
    content bytea not null,
    content_type text not null,
    -- The integrity value of the file: "sha384-" and the Base64 SHA-384 hash of the content.
    integrity text not null,
    size integer not null check (size >= 0),
    created_at timestamptz not null,
    primary key (extension_id, version, path)
);
