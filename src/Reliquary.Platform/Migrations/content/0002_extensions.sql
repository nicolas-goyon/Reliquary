-- The activation flag of each plugin for each user. The registry of the instance is users.extensions.
create table content.plugin_activations (
    owner_id uuid not null,
    extension_id text not null,
    enabled boolean not null,
    updated_at timestamptz not null,
    primary key (owner_id, extension_id)
);
