-- The extension registry of the instance: the installed plugins and modules. See docs/architecture/extensions.md.
create table users.extensions (
    id text primary key,
    kind text not null check (kind in ('plugin', 'module')),
    name text not null,
    version text not null,
    core_api text not null,
    permissions text[] not null default '{}',
    slots text[] not null default '{}',
    manifest jsonb not null,
    settings jsonb not null default '{}',
    -- The activation flag of the instance. A disabled extension does not run for any user.
    enabled boolean not null default false,
    installed_at timestamptz not null,
    updated_at timestamptz not null
);
